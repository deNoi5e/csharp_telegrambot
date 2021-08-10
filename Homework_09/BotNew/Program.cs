using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
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

namespace BotNew
{
    enum ReceivingState
    {
        WaitingMessage,
        WaitingCity,
        WaitingCommand,
        WaitingFileName
    }


    public static class Program
    {
        private static ReceivingState state = ReceivingState.WaitingMessage;
        private static TelegramBotClient Bot;
        private static WebClient WebClient;
        private static Root tempWeather;
        private static string Token { get; set; } = "222351205:AAEMBWyl4SoZ6NITzyWbdCKKsMI8D-gp0a4";
        private const string FileDir = @"UploadFiles";

        public static async Task Main()
        {
            Bot = new TelegramBotClient(Token);

            // Проверка наличия директории из константы FileDir, в случае отсутствия - директория будет создана.
            if (!Directory.Exists(FileDir)) Directory.CreateDirectory($"{FileDir}");


            var me = await Bot.GetMeAsync();
            Console.Title = me.Username;

            var cts = new CancellationTokenSource();

            // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
            Bot.StartReceiving(new DefaultUpdateHandler(HandleUpdateAsync, HandleErrorAsync),
                cts.Token);

            Console.WriteLine($"Start listening for @{me.Username}");
            Console.ReadLine();

            // Send cancellation request to stop bot
            cts.Cancel();
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
            CancellationToken cancellationToken)
        {
            var handler = update.Type
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
            Console.WriteLine($"Receive message type: {message.Type}");

            // Receive Document

            if (message.Type == MessageType.Document)
            {
                await UploadFile(message);
                return;
            }


            if (message.Type != MessageType.Text)
                return;


            switch (state)
            {
                case ReceivingState.WaitingCity:
                    await GetCity(message);
                    break;
                case ReceivingState.WaitingMessage:
                    var action = (message.Text.Split(' ').First()) switch
                    {
                        "/start" => StartMessage(message),
                        "/files" => SendListFiles(message),
                        "/weather" => RequestWeather(message),
                        _ => Usage(message)
                    };
                    var sentMessage = await action;
                    Console.WriteLine($"The message was sent with id: {sentMessage.MessageId}");
                    break;
                case ReceivingState.WaitingCommand:
                    Console.WriteLine("Ожидаем команду");
                    break;
                case ReceivingState.WaitingFileName:
                    try
                    {
                        await DownloadFile(message);
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        await Bot.SendTextMessageAsync(message.Chat.Id, 
                            "Неправильно, идем назад");
                        throw;
                    }
                    finally
                    {
                        state = ReceivingState.WaitingMessage;
                        
                    }
                    
                    
            }
            
            static async Task<Message> StartMessage(Message message)
            {
                state = ReceivingState.WaitingMessage;

                var startMessage =
                    "Привет!\n" +
                    "В данном боте реализовано домашнее задание курса \"C#-разработчик.\"\n" +
                    "Для работы используй следующие команды:\n\n" +
                    "/weather - для получения погоды;\n" +
                    "/files - для использования файлообменника;\n" +
                    "/start - для попадания в данное меню\n";
                await Bot.SendTextMessageAsync(message.Chat.Id, startMessage);

                // Simulate longer running task
                await Task.Delay(500);

                return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                    text: "Нажми на команду или введи вручную");
            }

            static async Task<Message> SendListFiles(Message message)
            {
                state = ReceivingState.WaitingFileName;

                try
                {
                    Console.WriteLine("Файлы:");
                    string[] files = Directory.GetFiles(FileDir);

                    StringBuilder sb = new StringBuilder();

                    foreach (var file in files)
                    {
                        sb.Append($"{file}\n");
                    }

                    var listFile = sb.ToString();

                    return await Bot.SendTextMessageAsync(message.Chat.Id,
                        listFile + "\n\nДля скачивания файла введи его имя, для отмены /cancel:\n",
                        replyMarkup: new ForceReplyMarkup(),
                        cancellationToken: default);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }


            static async Task<Message> Usage(Message message)
            {
                const string usage = "Введена некорректная команда!\n" +
                                     "/start   - вывести начальную информацию о работе с ботом\n" +
                                     "/help - помощь";
                return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                    text: usage,
                    replyMarkup: new ReplyKeyboardRemove());
            }
        }

        private static async Task DownloadFile(Message message)
        {
            var fileName = message.Text;
            using FileStream fileStream =
                new($"{FileDir}/{message.Text}", FileMode.Open, FileAccess.Read, FileShare.Read);

            await Bot.SendDocumentAsync(message.Chat.Id,
                document: new InputOnlineFile(fileStream, fileName),
                caption: $"Отправлено",
                cancellationToken: default);
            state = ReceivingState.WaitingMessage;
        }

        /// <summary>
        /// Сохранение файла типа document в папку с запущенным проектом
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private static async Task UploadFile(Message message)
        {
            var file = await Bot.GetFileAsync(message.Document.FileId);
            var fileName = message.Document.FileName;
            FileStream fs = new FileStream($"{FileDir}\\{fileName}", FileMode.Create);
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
                var city = message.Text;
                //await Task.Delay(1000);
                var sendText = GetWeather(city);
                var iconPng = "http://openweathermap.org/img/w/" + 
                              $"{tempWeather.Weather[0].Icon}" + ".png";
                //WebClient.DownloadFile(iconPng, $"{FileDir}\\icon.png");
                //Bitmap b = new Bitmap()
                

                await Bot.SendPhotoAsync(message.Chat.Id, 
                    $"{iconPng}",
                    caption: sendText);
                //await Bot.SendTextMessageAsync(message.Chat.Id, sendText);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                state = ReceivingState.WaitingMessage;
            }
        }

        /// <summary>
        /// Запрос City
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private static async Task<Message> RequestWeather(Message message)
        {
            state = ReceivingState.WaitingCity;

            return await Bot.SendTextMessageAsync(message.Chat.Id, "Введи название города!",
                replyMarkup: new ForceReplyMarkup());
        }

        /// <summary>
        /// Получение информации о погоде
        /// </summary>
        /// <param name="city">Город</param>
        /// <returns>Текстовое представление погоды</returns>
        private static string GetWeather(string city)
        {
            WebClient = new WebClient();
            const string weatherApiKey = "0ef64ffa4b4a21ed287172f79e03b1d4"; // токен для OpenWeatherMap
            
            var currentUrl = $"http://api.openweathermap.org/data/2.5/weather?q={city}" +
                             $"&mode=json&units=metric&APPID={weatherApiKey}";
            
            var weatherContent = WebClient.DownloadString(currentUrl);

            tempWeather = JsonConvert.DeserializeObject<Root>(weatherContent, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            });

      
            var textWeather = $"Погода в городе: {tempWeather.Name}\n" +
                              $@"{Convert.ToInt32(tempWeather.Main.Temp).ToString()}°C";
            return textWeather;
        }

        private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
            CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException =>
                    $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(errorMessage);
            return Task.CompletedTask;
        }
    }
}