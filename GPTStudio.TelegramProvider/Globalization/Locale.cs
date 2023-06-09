﻿namespace GPTStudio.TelegramProvider.Globalization;

internal enum Strings
{
    FirstHelloMsg,
    SuccessChangeLang,
    MainMenuTitle,
    SettingsTitle,
    LanguagesMenuTitle,
    ModesMenuTitle,
    AdminPanelTitle,
    ServicesMenuTitle,

    MainMenuStartChatting,
    MainMenuSettings,
    MainMenuAbout,
    MainMenuSummary,
    MainMenuAdminPanel,

    SettingsLanguage,
    SettingsGenMode,
    SettingsModelsSettings,
    BackToSettingsTitle,
    BackToMainTitle,
    BackToModesTitle,
    Back,

    StartChattingMsg,
    StopGenerationMsg,
    ResponseGenMsg,
    StreamGenModeMsg,
    FullyGenModeMsg,
    RequestErrorMsg,

    SummaryForMsg,
    SummaryMemberSince,
    SummaryTokensGen,
    SummaryRequests,

}

internal static class Locale
{
    public static string[] SupportedLocales = new[] { "en","uk","ru" };
    public static readonly Dictionary<string, Dictionary<Strings, string>> Cultures = new()
    {
        {SupportedLocales[0], new Languages.EN().Culture },
        {SupportedLocales[1], new Languages.UK().Culture },
        {SupportedLocales[2], new Languages.RU().Culture },
    };
}
