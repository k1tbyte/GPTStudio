﻿using GPTStudio.OpenAI.Chat;
using GPTStudio.OpenAI.Models;
using GPTStudio.TelegramProvider.Database.Models;
using GPTStudio.TelegramProvider.Globalization;
using GPTStudio.TelegramProvider.Infrastructure;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Telegram.Bot;
using Env = GPTStudio.TelegramProvider.Infrastructure.Configuration;

namespace GPTStudio.TelegramProvider.Utils;
internal static partial class Common
{

    /// <summary>
    /// If you specify a user message, the chat context will not be taken into account
    /// </summary>
    /// <param name="chat"></param>
    /// <param name="user"></param>
    /// <param name="userMessage"></param>
    /// <returns></returns>
    public static ChatRequest GenerateChatRequest(GChat chat,GUser user, GChatMessage? userMessage = null)
    {
        int totalTokens = 0;
        List<GChatMessage> msgList = new();


        if (!string.IsNullOrEmpty(user.ChatMode.SystemMessage))
        {
            totalTokens = Env.Tokenizer.Calculate(user.ChatMode.SystemMessage);
            msgList.Add(new GChatMessage(0, user.ChatMode.SystemMessage, null) { Role = Role.System });
        }

        if(userMessage == null)
        {
            for (int i = chat.Messages.Count - 1, insertIndex = totalTokens == 0 ? 0 : 1; i >= 0; i--)
            {
                var gMsg = chat.Messages[i];

                if (gMsg.MessageType != GMessageType.Text)
                    continue;
                if ((totalTokens + gMsg.Tokens) > 3000)
                    break;

                totalTokens += gMsg.Tokens;
                msgList.Insert(insertIndex, chat.Messages[i]);
            }
        }
        else msgList.Add(userMessage);

        return new ChatRequest(msgList, Model.GPT3_5_Turbo, maxTokens: user.ChatMode.MaxTokens);
    }


    /// <summary>
    ///  If editMsgId not null message editing with error
    /// </summary>
    public static async void NotifyOfRequestError(long chatId, GUser user,Exception e,int? editMsgId = null, int? replyMsgId = null)
    {
        Logger.PrintError(e.ToString());
        if (editMsgId != null)
            await Env.Client.EditMessageTextAsync(chatId, editMsgId.Value, Locale.Cultures[user.LocaleCode][Strings.RequestErrorMsg]).ConfigureAwait(false);
        else
            await Env.Client.SendTextMessageAsync(chatId, Locale.Cultures[user.LocaleCode][Strings.RequestErrorMsg],replyToMessageId: replyMsgId).ConfigureAwait(false);
    }


    public static async Task<string> ExtractTextFromImage(MemoryStream stream)
    {
        App.HttpClient.DefaultRequestHeaders.Clear();
        App.HttpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", Env.Props.Azure.ComputerVisionKey);
        var uri =
            $"https://{Env.Props.Azure.ComputerVisionServiceName}.cognitiveservices.azure.com/computervision/imageanalysis:analyze?api-version=2023-02-01-preview&features=read";

        stream.Position = 0;
        using var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await App.HttpClient.PostAsync(uri, content);
        var jobject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
        return jobject["readResult"]["content"].Value<string>();
    }

    public static Stream StreamFromString(string s)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write(s);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    [GeneratedRegex("^(-\\d|\\d+){1,9}$")]
    public static partial Regex Integer();

    public static bool SetPropertyValue(this object obj, string propName, object? value)
    {
        var index = propName.IndexOf('.');
        if (index != -1)
            return obj.GetType().GetProperty(propName[..index])?.GetValue(obj, null)?.SetPropertyValue(propName[(index + 1)..],value) == true;

        var property = obj.GetType().GetProperty(propName);
        if (property is null || (value != default && property.GetValue(obj, null) != default && !TryCast(ref value, property.GetValue(obj, null))))
            return false;

       property.SetValue(obj, value, null);
       return true;
    }

    public static object? GetPropertyValue(this object obj, string propName)
    {
        var index = propName.IndexOf('.');
        if (index != -1)
           return obj.GetType().GetProperty(propName[..index])?.GetValue(obj, null)?.GetPropertyValue(propName[(index + 1)..]);

        return obj.GetType().GetProperty(propName)?.GetValue(obj, null);
    }

    public static bool TryCast(ref object? obj, object? to)
    {
        if (obj == null || to == null)
            return false;

        var conversionType = to.GetType();
        if (obj.GetType() == conversionType)
            return true;

        try
        {
            obj = Convert.ChangeType(obj, conversionType);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string[] SplitCamelCase(string input)
    {
        return Regex.Replace(input, "(?<=[a-z])([A-Z])", " $1", RegexOptions.Compiled).Split(' ');
    }

    public static string ToReadable(this TimeSpan span)
    {
        string formatted = string.Format("{0}{1}{2}{3}",
            span.Duration().Days > 0 ? string.Format("{0:0} d, ", span.Days) : string.Empty,
            span.Duration().Hours > 0 ? string.Format("{0:0} h, ", span.Hours) : string.Empty,
            span.Duration().Minutes > 0 ? string.Format("{0:0} m, ", span.Minutes) : string.Empty,
            span.Duration().Seconds > 0 ? string.Format("{0:0} s", span.Seconds) : string.Empty);

        if (formatted.EndsWith(", ")) formatted = formatted.Substring(0, formatted.Length - 2);

        if (string.IsNullOrEmpty(formatted)) formatted = "0 seconds";

        return formatted;
    }

    public static string ToReadable(this Version version)
    {
        return $"v. {version}{(version.Revision == 0 ? "" : " Pre-release")}";
    }

    public static void CreateDirIfNotExists(string path)
    {
        if (Directory.Exists(path))
            return;

        Directory.CreateDirectory(path);
    }

    public static void ExecConsoleCommand(string command,int? sleep = null)
    {
        using var process = new Process();
        process.StartInfo.UseShellExecute = false;
        if (OperatingSystem.IsWindows())
        {
            process.StartInfo.FileName = "cmd";
            process.StartInfo.Arguments = $"/c \"{(sleep.HasValue ? $"timeout {sleep} /nobreak > nul &&" : null)} {command}\"";
        }
        else
        {
            process.StartInfo.FileName = OperatingSystem.IsLinux() ? "/bin/bash" : "/System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal";
            process.StartInfo.Arguments = $"-c \"{(sleep.HasValue ? $"sleep {sleep} &&" : null)} {command}\"";
        }

        process.Start();
    }

    public static async Task DownloadFileToStream(string fileid, MemoryStream stream)
    {
        await Env.Client.DownloadFileAsync((await Env.Client.GetFileAsync(fileid).ConfigureAwait(false)).FilePath!, stream).ConfigureAwait(false);
        stream.Position = 0;
    }
}
