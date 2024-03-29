﻿using BotNew;
using Newtonsoft.Json;
using Renci.SshNet;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using File = Telegram.Bot.Types.File;
using Serilog;
using Serilog.Core;

namespace TeleSharp;

internal enum ReceivingState
{
    WaitingMessage,
    WaitingCity,
    WaitingCommand,
    WaitingFileName,
    WaitingExecution
}

public static class TeleSharp
{
    private static ReceivingState _state = ReceivingState.WaitingMessage;
    private static TelegramBotClient Bot { get; set; }
    private static HttpClient HttpClient { get; set; }
    private static Root TempWeather { get; set; }
    private static string Token { get; set; }
    private static string SshLogin { get; set; }
    private static string SshPassword { get; set; }
    private static string FileDir { get; set; } = @"UploadFiles";
    private static SshClient SshClient { get; set; }
    private static DateTime StartBotTime { get; } = DateTime.Now;

    private static readonly LoggingLevelSwitch levelSwitch
        = new(Serilog.Events.LogEventLevel.Information);
    private static readonly Logger logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console()
            .WriteTo.File("log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

    /// <summary>
    /// Основной процесс
    /// </summary>
    /// <param name="args">Telegram Token</param>
    /// <returns></returns>
    public static async Task Main(string[] args)
    {
        if (args.Length < 3)
        {
            logger.Fatal("Не задан параметры запуска");
            return;
        }
        

        Token = args[0];
        SshLogin = args[1];
        SshPassword = args[2];

        Bot = new TelegramBotClient(Token);
        
        PasswordConnectionInfo connectionInfo = new("192.168.1.1", SshLogin, SshPassword);
        SshClient = new SshClient(connectionInfo);
        connectionInfo.Timeout = TimeSpan.FromSeconds(30);

        

        SshClient.Connect();

        // Проверка наличия директории из константы FileDir, в случае отсутствия - директория будет создана.
        if (!Directory.Exists(FileDir)) Directory.CreateDirectory($"{FileDir}");


        User me = await Bot.GetMeAsync();
        Console.Title = me.Username;


        CancellationTokenSource cts = new();

        // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
        Bot.StartReceiving(new DefaultUpdateHandler(HandleUpdateAsync, HandleErrorAsync),
            cts.Token);

        logger.Information($"Start listening for @{me.Username}");
        await Bot.SendTextMessageAsync(chatId: new ChatId(36327828), 
            text: $"Бот запущен {StartBotTime}", 
            cancellationToken: default);


        Console.CancelKeyPress += (s, e) => cts.Cancel();
        await Task.Delay(-1, cts.Token).ContinueWith(t => { }, cts.Token);
        // Send cancellation request to stop bot
        //cts.Cancel();
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cancellationToken)
    {
        Task handler = update.Type
            switch
            {
                UpdateType.Message => BotOnMessageReceived(update.Message),
                _ => throw new ArgumentOutOfRangeException()
            };

        try
        {
            await handler;
        }
        catch (Exception exception)
        {
            await HandleErrorAsync(botClient, exception, cancellationToken);
        }
    }

    private static async Task BotOnMessageReceived(Message message)
    {
        logger.Information($"Receive message type: {message.Type}");

        // Receive Document

        if (message.Type == MessageType.Document)
        {
            await UploadFile(message);
            return;
        }


        if (message.Type != MessageType.Text)
            return;


        switch (_state)
        {
            case ReceivingState.WaitingCity:
                await GetCity(message);
                break;
            case ReceivingState.WaitingExecution:

            case ReceivingState.WaitingMessage:
                var action = message.Text.Split(' ').First() switch
                {
                    "/start" => StartMessage(message),
                    "/files" => SendListFiles(message),
                    "/vpnon" => ConnectOpenVpn(message),
                    "/rebootrouter" => RebootRouter(message),
                    "/rebootorange" => RebootOrange(message),
                    "/vpnoff" => DisconnectOpenVpn(message),
                    "/weather" => RequestWeather(message),
                    "/ssh" => SshReconnect(message),
                    "/df" => DiskFree(message),
                    _ => Usage(message)
                };
                Message sentMessage = await action;
                logger.Information($"The message was sent with id: {sentMessage.MessageId}");
                break;
            case ReceivingState.WaitingCommand:
                logger.Information("Ожидаем команду");
                break;
            case ReceivingState.WaitingFileName:
                try
                {
                    await DownloadFile(message);
                    break;
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                    await Bot.SendTextMessageAsync(message.Chat.Id,
                        "Неправильно, идем назад");
                    throw;
                }
                finally
                {
                    _state = ReceivingState.WaitingMessage;
                }


            default:
                throw new ArgumentOutOfRangeException();
        }

        static async Task<Message> StartMessage(Message message)
        {
            _state = ReceivingState.WaitingMessage;

            return await Bot.SendTextMessageAsync(message.Chat.Id, $"Bot started on {StartBotTime}");

        }

        static async Task<Message> SendListFiles(Message message)
        {
            _state = ReceivingState.WaitingFileName;

            try
            {
                logger.Information("Файлы:");
                string[] files = Directory.GetFiles(FileDir);

                StringBuilder sb = new();

                foreach (string file in files) sb.Append($"{file}\n");

                string listFile = sb.ToString();

                return await Bot.SendTextMessageAsync(message.Chat.Id,
                    listFile + "\n\nДля скачивания файла введи его имя, для отмены /cancel:\n",
                    replyMarkup: new ForceReplyMarkup(),
                    cancellationToken: default);
            }
            catch (Exception e)
            {
                logger.Error(e.ToString());
                throw;
            }
        }


        static async Task<Message> Usage(Message message)
        {
            const string usage = "Введена некорректная команда!";
            return await Bot.SendTextMessageAsync(message.Chat.Id,
                usage,
                replyMarkup: new ReplyKeyboardRemove());
        }
    }

    /// <summary>
    /// Возврат данных занятого пространства на апельсинке
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    private static async Task<Message> DiskFree(Message message)
    {
        string result = "";
        Process process = new();
        process.StartInfo.FileName = "/bin/bash";
        process.StartInfo.Arguments = "-c \" " + "df -h" + " \"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.Start();

        result += process.StandardOutput.ReadToEnd();

        process.WaitForExit();

        return await Bot.SendTextMessageAsync(message.Chat.Id,
            result);

    }

    /// <summary>
    /// Принудительная переподключение к SSH роутера
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    private static async Task<Message> SshReconnect(Message message)
    {
        SshClient.Connect();
        return await Bot.SendTextMessageAsync(message.Chat.Id, 
            "SSH-клиент перезапущен", 
            cancellationToken: default);
    }

    private static async Task<Message> RebootRouter(Message message)
    {
        _state = ReceivingState.WaitingExecution;


        if (SshClient.IsConnected)
        {
            SshClient.RunCommand("reboot");
            Thread.Sleep(100);
        }
        else
        {
            return await Bot.SendTextMessageAsync(message.Chat.Id,
                "SSH connection NOTactive");
        }


        return await Bot.SendTextMessageAsync(message.Chat.Id,
            "Роутер отправлен в перезагрузку.");
    }

    private static async Task<Message> RebootOrange(Message message)
    {
        _state = ReceivingState.WaitingExecution;

        return await Bot.SendTextMessageAsync(message.Chat.Id,
            "Команда нереализована");
    }

    private static async Task<Message> ConnectOpenVpn(Message message)
    {
        _state = ReceivingState.WaitingExecution;


        if (SshClient.IsConnected)
        {
            SshClient.RunCommand("/etc/init.d/openvpn start");
            Thread.Sleep(100);
        }
        else
        {
            return await Bot.SendTextMessageAsync(message.Chat.Id,
                "SSH connection NOTactive");
        }


        return await Bot.SendTextMessageAsync(message.Chat.Id,
            "VPN включен");
    }

    private static async Task<Message> DisconnectOpenVpn(Message message)
    {
        _state = ReceivingState.WaitingExecution;


        if (SshClient.IsConnected)
        {
            SshClient.RunCommand("/etc/init.d/openvpn stop");
            Thread.Sleep(100);
        }
        else
        {
            return await Bot.SendTextMessageAsync(message.Chat.Id,
                "SSH connection NOTactive");
        }


        return await Bot.SendTextMessageAsync(message.Chat.Id,
            "VPN отключен");
    }


    private static async Task DownloadFile(Message message)
    {
        string fileName = message.Text;
        await using FileStream fileStream =
            new($"{FileDir}/{message.Text}", FileMode.Open, FileAccess.Read, FileShare.Read);

        await Bot.SendDocumentAsync(message.Chat.Id,
            new InputOnlineFile(fileStream, fileName),
            caption: $"Отправлено",
            cancellationToken: default);
        _state = ReceivingState.WaitingMessage;
    }

    /// <summary>
    /// Сохранение файла типа document в папку с запущенным проектом
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    private static async Task UploadFile(Message message)
    {
        File file = await Bot.GetFileAsync(message.Document.FileId);
        string fileName = message.Document.FileName;

        if (message.Document.MimeType == "application/x-bittorrent")
        {
            FileDir = @"/mnt/usb/torrents";
            if (!Directory.Exists(FileDir)) Directory.CreateDirectory(FileDir);
        }


        FileStream fs = new($"{FileDir}//{fileName}", FileMode.Create);
        await Bot.DownloadFileAsync(file.FilePath, fs);
        fs.Close();
        await fs.DisposeAsync();
        await Bot.SendTextMessageAsync(message.Chat.Id, "Файл сохранен.");
    }

    /// <summary>
    /// Получение "ожидаемого" City
    /// </summary>
    /// <param name="message"></param>
    private static async Task GetCity(Message message)
    {
        try
        {
            string city = message.Text;
            string sendText = await GetWeather(city);
            string iconPng = "http://openweathermap.org/img/w/" +
                             $"{TempWeather.Weather[0].Icon}" + ".png";

            await Bot.SendPhotoAsync(message.Chat.Id,
                $"{iconPng}",
                sendText);
        }
        catch (Exception e)
        {
            logger.Error(e.ToString());
            throw;
        }
        finally
        {
            _state = ReceivingState.WaitingMessage;
        }
    }

    /// <summary>
    /// Запрос City
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    private static async Task<Message> RequestWeather(Message message)
    {
        _state = ReceivingState.WaitingCity;

        return await Bot.SendTextMessageAsync(message.Chat.Id, "Введи название города!",
            replyMarkup: new ForceReplyMarkup());
    }

    /// <summary>
    /// Получение информации о погоде
    /// </summary>
    /// <param name="city">Город</param>
    /// <returns>Текстовое представление погоды</returns>
    private static async Task<string> GetWeather(string city)
    {
        HttpClient = new HttpClient();
        const string weatherApiKey = "0ef64ffa4b4a21ed287172f79e03b1d4"; // токен для OpenWeatherMap

        string currentUrl = $"http://api.openweathermap.org/data/2.5/weather?q={city}" +
                            $"&mode=json&units=metric&APPID={weatherApiKey}";

        HttpResponseMessage response = await HttpClient.GetAsync(currentUrl);
        string weatherContent = await response.Content.ReadAsStringAsync();

        TempWeather = JsonConvert.DeserializeObject<Root>(weatherContent, new JsonSerializerSettings
        {
            Formatting = Formatting.Indented
        });


        if (TempWeather == null) return null;

        string textWeather = $"Погода в городе: {TempWeather.Name}\n" +
                             $@"{Convert.ToInt32(TempWeather.Main.Temp)}°C";
        return textWeather;
    }

    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        string errorMessage = exception switch
        {
            ApiRequestException apiRequestException =>
                $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        logger.Error(errorMessage);
        return Task.CompletedTask;
    }
}