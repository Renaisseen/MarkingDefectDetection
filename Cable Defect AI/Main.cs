using System;
using CableDefect.Interface;
using CableDefect.OCR;
using System.Configuration;

class CableDefectGeneral
{
    public static class AppConfig
    {
        public static string TelegramBotToken
        {
            get { return ConfigurationManager.AppSettings.Get("TelegramKey"); }
        }
    }
    static void Main(string[] args)
    {
        TelegramHandler.Start(AppConfig.TelegramBotToken);
        return;
    }
}

