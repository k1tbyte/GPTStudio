﻿using GPTStudio.OpenAI.Tokenizer;
using GPTStudio.OpenAI;
using GPTStudio.TelegramProvider.Database;
using GPTStudio.TelegramProvider.Database.Models;
using MongoDB.Bson;
using Telegram.Bot;
using Telegram.Bot.Types;
using Amazon.Runtime.Internal;
using GPTStudio.TelegramProvider.Globalization.Languages;
using Telegram.Bot.Exceptions;
using GPTStudio.TelegramProvider.Common;
using GPTStudio.TelegramProvider.Commands;

namespace GPTStudio.TelegramProvider.Infrastructure;
internal static class App
{
    public static readonly HashSet<long> NowGeneration = new();
    public static readonly GPTTokenizer Tokenizer      = new(Properties.Resources.TokenizerMerges);

    public static Config? Config { get; private set; }
    public static TelegramBotClient? Client { get; private set; }
    public static OpenAIClient? GPTClient { get; private set; }


    public static async Task Init()
    {
        Config = await Config.Load();
        Config.CheckDefaultConfigValues();

        GPTClient = new(Config.OpenAIApiKey!);
        Client    = new(Config.TelegramBotToken!);
        Connection.Connect();

        Client.StartReceiving(OnUpdateHandler, OnErrorHandler);
        Logger.Print($"Telegram update callback processing...");
    }

    private static async Task OnUpdateHandler(ITelegramBotClient sender, Update e, CancellationToken cancellationToken)
    {
        var senderUser = e.Message?.From == null ? e.CallbackQuery?.From : e.Message.From;
        var chatId = e.Message?.From == null ? e.CallbackQuery?.Message?.Chat.Id : e.Message.Chat.Id;

        if (senderUser == null)
            return;
        if (!Connection.Users.FindFirst(o => o.Id == senderUser.Id, out GUser user))
        {
            var isSupportedLang = Locale.SupportedLocales.Contains(senderUser.LanguageCode);

            Logger.Print("OnUpdateHandler() | Joined new user: @" + senderUser.Username + " , ID: " + senderUser.Id, endlStart: true, beforeCommand: true);
            Connection.Users.InsertOne(user = new GUser(senderUser.Id) { LocaleCode = isSupportedLang ? senderUser.LanguageCode : null });

            if (chatId != null)
                await Env.Client.SendTextMessageAsync(chatId, $"{Locale.Cultures[user.LocaleCode!][Strings.FirstHelloMsg]} {senderUser.FirstName} ?");
        }
        else if (senderUser.Username != user.Username)
        {
            Connection.Users.UpdateOne(o => o.Id == senderUser.Id, Builders<GUser>.Update.Set(nameof(user.Username), senderUser.Username));
        }

#if DEBUG
        if (user.IsAdmin != true)
            return;
#endif

        if (e.Type == UpdateType.CallbackQuery)
        {
            await CallbackHandler.HandleCallbackQuery(e.CallbackQuery!, user);
            return;
        }

        if (user.LastCommand != WaitCommand.None && e.Message?.Text != "/cancel")
        {
            CommandHandler.HandleWaitCommand(e.Message, user);
            return;
        }

        if (e.Message!.Type == MessageType.Voice)
        {
            e.Message.Text = await VoiceRecognizer.RecognizeVoice(e.Message.Voice!);
        }
        else if (e.Message.Type != MessageType.Text)
            return;

        if (!Connection.Chats.FindFirst(o => o.Id == e.Message.Chat.Id, out GChat chat))
            Connection.Chats.InsertOne(chat = new(e.Message.Chat.Id));

        switch (e.Type)
        {
            case UpdateType.Message:
                if (e.Message.Text?.StartsWith('/') == true)
                    await CommandHandler.HandleCommand(e.Message, user);
                else
                    HandleTextMessage(e.Message, chat, user);
                break;
        }
    }

    private static async void HandleTextMessage(Telegram.Bot.Types.Message msg, GChat chat, GUser user)
    {
        if (NowGeneration.Contains(msg.From!.Id) || await CommonHelpers.IsQuotaExceeded(msg, user))
            return;

        NowGeneration.Add(msg.From.Id);

        var lastMsg = new GChatMessage(msg.MessageId, msg.Text, msg.From.Id) { Tokens = Env.Tokenizer.Calculate(msg.Text), Role = Role.User };
        if (lastMsg.Tokens > 3000)
            return;

        chat.Messages.Add(lastMsg);

        var button = InlineKeyboardButton.WithCallbackData(Locale.Cultures[user.LocaleCode][Strings.StopGenerationMsg], $"stop.{msg.From.Id}");
        var request = Common.GenerateChatRequest(chat, user, user.ChatMode.IgnoreChatHistory ? lastMsg : null);
        var response = new StringBuilder();
        var sendedMsg = user.GenFullyMode == true ?
              await Env.Client.SendTextMessageAsync(msg.Chat.Id, Locale.Cultures[user.LocaleCode][Strings.ResponseGenMsg], replyToMessageId: msg.MessageId, replyMarkup: new InlineKeyboardMarkup(button)).ConfigureAwait(false)
            : await Env.Client.SendTextMessageAsync(msg.Chat.Id, ". . .", replyToMessageId: msg.MessageId).ConfigureAwait(false);

        var lastEdit = DateTimeOffset.Now.ToUnixTimeSeconds();
        var lastEditMsgLength = 0;
        var failCounter = 0;
        using var cancelToken = new CancellationTokenSource();

        try
        {
            await Env.GPTClient.ChatEndpoint.StreamCompletionAsync(request, result =>
            {
                if (!NowGeneration.Contains(msg.From.Id))
                {
                    cancelToken.Cancel();
                    return;
                }

                if (String.IsNullOrEmpty(result.FirstChoice))
                {
                    failCounter++;
                    if (failCounter == 20)
                        throw new Exception("Too many failures while response generating");

                    return;
                }


                response.Append(result.FirstChoice);

                if (user.GenFullyMode == true)
                    return;

                var offset = DateTimeOffset.Now.ToUnixTimeSeconds();
                if ((offset - lastEdit) >= 1)
                {
                    lastEdit = offset;
                    lastEditMsgLength = response.Length;
                    Env.Client.EditMessageTextAsync(msg.Chat.Id, sendedMsg.MessageId, response.ToString(), replyMarkup: button);
                }

            }, cancelToken.Token).ConfigureAwait(false);

            var responseContent = response.ToString();
            var responseTokens = Env.Tokenizer.Calculate(responseContent);
            var userDocument = new BsonDocument("_id", msg.From.Id);

            if (response.Length != lastEditMsgLength || cancelToken.IsCancellationRequested || user.GenFullyMode != true)
            {
                try
                {
                    await FilanizeGeneration(responseContent);
                }
                catch (Exception exc)
                {
                    if (exc.ToString().Contains("can't parse entities: Can't find end of the entity"))
                        await FilanizeGeneration(responseContent, null);
                }
            }


            user.ChatMode.Quota.Used += responseTokens;
            Connection.Users.UpdateOne(userDocument, Builders<GUser>.Update.Inc("TotalTokensGenerated", responseTokens));
            Connection.Users.UpdateOne(userDocument, Builders<GUser>.Update.Set(nameof(GUser.ChatMode), user.ChatMode));

            if (responseContent.Length > 500)
            {
                responseContent = responseContent[..500];
                responseTokens = Env.Tokenizer.Calculate(responseContent);
            }

            GChat.PushMessage(msg.Chat.Id, lastMsg);
            GChat.PushMessage(msg.Chat.Id, new(sendedMsg.MessageId, responseContent, null)
            {
                Tokens = responseTokens,
                Role = Role.Assistant
            });


            Connection.Users.UpdateOne(userDocument, Builders<GUser>.Update.Inc("TotalRequests", 1));
        }
        catch (Exception e)
        {
            Common.NotifyOfRequestError(chat.Id, user, e, sendedMsg.MessageId);
        }

        NowGeneration.Remove(msg.From.Id);

        async Task FilanizeGeneration(string responseContent, ParseMode? mode = ParseMode.Markdown)
        {
            await Env.Client.EditMessageTextAsync(msg.Chat.Id, sendedMsg!.MessageId,
            cancelToken!.IsCancellationRequested ? response!.Append(". . .").ToString() : responseContent,
            parseMode: mode).ConfigureAwait(false);
        }
    }

    private async static Task OnErrorHandler(ITelegramBotClient sender, Exception e, CancellationToken cancellationToken)
    {
        if (e is ApiRequestException apiException && apiException.ErrorCode == 404)
        {
            Logger.PrintError("Invalid telegram bot token");
            Config.TelegramBotToken = null;
            Config.Save();
            Program.Restart();
        }
        Logger.PrintError(e.ToString());
    }

    /*    private static async Task GenerateImage(Telegram.Bot.Types.Message msg, string prompt, int imgCount = 1)
    {
        var waitMsg = await Env.Client.SendTextMessageAsync(msg.Chat.Id, "Generating . . .");
        try
        {
            var result = await Env.GPTClient.ImagesEndPoint.GenerateImageAsync(prompt, numberOfResults: imgCount);

            if (result.Count > 1)
            {
                var list = new List<IAlbumInputMedia>();
                foreach (var item in result)
                    list.Add(new InputMediaPhoto(item!));
                await Env.Client.SendMediaGroupAsync(msg.Chat.Id, list, replyToMessageId: msg.MessageId);
            }
            else
                await Env.Client.SendPhotoAsync(msg.Chat.Id, new(result[0]!), replyMarkup: KeyboardBuilder.ImageGenerateMarkup, caption: prompt);

            await Env.Client.DeleteMessageAsync(msg.Chat.Id, waitMsg.MessageId);
            Connection.Users.UpdateOne(new BsonDocument("_id", msg.From.Id), Builders<GUser>.Update.Inc("TotalRequests", 1));
        }
        catch
        {
            await Env.Client.EditMessageTextAsync(msg.Chat.Id, waitMsg.MessageId, "An error occurred while generating");
        }
    }*/
}
