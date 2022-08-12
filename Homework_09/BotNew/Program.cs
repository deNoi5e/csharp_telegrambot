using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BotNew;
using Newtonsoft.Json;
using Renci.SshNet;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using File = Telegram.Bot.Types.File;

namespace TeleSharp
{
    internal enum ReceivingState
    {
        WaitingMessage,
        WaitingCity,
        WaitingCommand,
        WaitingFileName,
        WaitingExecution
    }


    public static class Program
    {
        private static ReceivingState _state = ReceivingState.WaitingMessage;
        public static TelegramBotClient Bot;
        public static WebClient WebClient;
        public static Root TempWeather;
        private static string Token { get; set; } //= "222351205:AAEMBWyl4SoZ6NITzyWbdCKKsMI8D-gp0a4";
        private static string FileDir { get; set; } = @"UploadFiles";

        public static PasswordConnectionInfo ConnectionInfo =
            new PasswordConnectionInfo("192.168.1.1", 22, "root", "LZw05p0j");

        internal static SshClient SshClient = new SshClient(ConnectionInfo);

        public static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Не задан Токен в качестве аргумента");
                return;
            }

            Token = args[0];
            Bot = new TelegramBotClient(Token);
            SshClient.Connect();
            ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);

            // Проверка наличия директории из константы FileDir, в случае отсутствия - директория будет создана.
            if (!Directory.Exists(FileDir)) Directory.CreateDirectory($"{FileDir}");


            User me = await Bot.GetMeAsync();
            Console.Title = me.Username;


            CancellationTokenSource cts = new CancellationTokenSource();

            // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
            Bot.StartReceiving(new DefaultUpdateHandler(HandleUpdateAsync, HandleErrorAsync),
                cts.Token);

            Console.WriteLine($"Start listening for @{me.Username}");
            //Console.ReadLine();

            Console.CancelKeyPress += (s, e) => cts.Cancel();
            await Task.Delay(-1, cts.Token).ContinueWith(t => { });
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
            Console.WriteLine($"Receive message type: {message.Type}");

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
                    var action = (message.Text.Split(' ').First()) switch
                    {
                        "/start" => StartMessage(message),
                        "/files" => SendListFiles(message),
                        "/vpnon" => ConnectOpenVpn(message),
                        "/rebootrouter" => RebootRouter(message),
                        "/rebootorange" => RebootOrange(message),
                        "/vpnoff" => DisconnectOpenVpn(message),
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
                        _state = ReceivingState.WaitingMessage;
                    }


                default:
                    throw new ArgumentOutOfRangeException();
            }

            static async Task<Message> StartMessage(Message message)
            {
                _state = ReceivingState.WaitingMessage;

                await Bot.SendTextMessageAsync(message.Chat.Id, $"Bot started on {DateTime.Now}");

                // Simulate longer running task
                await Task.Delay(500);

                return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                    text: "Нажми на команду или введи вручную");
            }

            static async Task<Message> SendListFiles(Message message)
            {
                _state = ReceivingState.WaitingFileName;

                try
                {
                    Console.WriteLine("Файлы:");
                    string[] files = Directory.GetFiles(FileDir);

                    StringBuilder sb = new StringBuilder();

                    foreach (var file in files)
                    {
                        sb.Append($"{file}\n");
                    }

                    string listFile = sb.ToString();

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
                return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                    "SSH connection NOTactive");
            }


            return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                text: "Роутер отправлен в перезагрузку.");
        }
        private static async Task<Message> RebootOrange(Message message)
        {
            _state = ReceivingState.WaitingExecution;

            return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                text: "Команда нереализована");
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
                return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                    "SSH connection NOTactive");
            }


            return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                text: "VPN включен");
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
                return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                    "SSH connection NOTactive");
            }


            return await Bot.SendTextMessageAsync(chatId: message.Chat.Id,
                text: "VPN отключен");
        }


        private static async Task DownloadFile(Message message)
        {
            string fileName = message.Text;
            await using FileStream fileStream =
                new($"{FileDir}/{message.Text}", FileMode.Open, FileAccess.Read, FileShare.Read);

            await Bot.SendDocumentAsync(message.Chat.Id,
                document: new InputOnlineFile(fileStream, fileName),
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
                FileDir = @"UploadFiles/torrents";
                if (!Directory.Exists(FileDir))
                {
                    Directory.CreateDirectory(FileDir);
                }
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
                //await Task.Delay(1000);
                string sendText = GetWeather(city);
                string iconPng = "http://openweathermap.org/img/w/" +
                                 $"{TempWeather.Weather[0].Icon}" + ".png";
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
        private static string GetWeather(string city)
        {
#pragma warning disable SYSLIB0014
            WebClient = new WebClient();
#pragma warning restore SYSLIB0014
            const string weatherApiKey = "0ef64ffa4b4a21ed287172f79e03b1d4"; // токен для OpenWeatherMap

            string currentUrl = $"http://api.openweathermap.org/data/2.5/weather?q={city}" +
                                $"&mode=json&units=metric&APPID={weatherApiKey}";

            string weatherContent = WebClient.DownloadString(currentUrl);

            TempWeather = JsonConvert.DeserializeObject<Root>(weatherContent, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            });


            if (TempWeather != null)
            {
                string textWeather = $"Погода в городе: {TempWeather.Name}\n" +
                                     $@"{Convert.ToInt32(TempWeather.Main.Temp)}°C";
                return textWeather;
            }

            return null;
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

            Console.WriteLine(errorMessage);
            return Task.CompletedTask;
        }
    }
}