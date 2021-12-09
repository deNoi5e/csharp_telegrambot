using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;

#pragma warning disable 618


namespace Homework_09
{
    // Состояния для приема сообщений
    enum ReceivingState
    {
        WaitingMessage,
        WaitingCity,
        WaitingCommand,
        WaitingFileName,
        GetDocument
    }

    class Program
    {
        private static TelegramBotClient client;
        private static string Token { get; set; } = "222351205:AAEMBWyl4SoZ6NITzyWbdCKKsMI8D-gp0a4";
        private static ReceivingState state = ReceivingState.WaitingMessage;
        private const string FileDir = "UploadFiles";

        static void Main(string[] args)
        {
            client = new TelegramBotClient(Token);

            client.StartReceiving();
            client.OnMessage += OnMessageHandler;


            Console.ReadLine();
            client.StopReceiving();
        }

        /// <summary>
        /// Обработка входящих сообщений
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static async void OnMessageHandler(object sender, MessageEventArgs e)
        {
            var msg = e.Message;

            if (msg.Text != null)
            {
                Console.WriteLine($"Пришло сообщение с текстом {msg.Text}");

                switch (state)
                {
                    case ReceivingState.WaitingCommand:
                        Console.WriteLine("Ожидается ввод команды:");
                        break;
                    case ReceivingState.WaitingMessage:
                        var menu =
                            "Ожидание команды /weather или /files";

                        await client.SendTextMessageAsync(msg.Chat.Id, menu);

                        state = ReceivingState.WaitingCommand;

                        break;
                    case ReceivingState.WaitingCity:
                        try
                        {
                            await SendWeather(msg);
                        }
                        catch (Exception)
                        {
                            state = ReceivingState.WaitingCommand;
                            throw;
                        }



                        state = ReceivingState.WaitingCommand;

                        break;
                    case ReceivingState.WaitingFileName:
                        {
                            state = ReceivingState.WaitingCommand;

                            if (msg.Text == "/cancel")
                            {
                                break;
                            }

                            try
                            {
                                FileStream fileStream =
                                new($"{FileDir}/{msg.Chat.Id}/{msg.Text}", FileMode.Open, FileAccess.Read, FileShare.Read);

                                string filename = msg.Text;
                                await client.SendDocumentAsync(msg.Chat.Id,
                                    document: new InputOnlineFile(fileStream, filename),
                                    caption: "Отправлено");

                                break;
                            }
                            catch (Exception fileError)
                            {
                                System.Console.WriteLine(fileError);
                                await client.SendTextMessageAsync(msg.Chat.Id, "Введено не существующее название файла!");
                                break;
                            }


                        }
                }
            }

            // Обработка сообщения с типом "Документ" 
            if (msg.Document != null)
            {
                state = ReceivingState.GetDocument;

                CheckUploadFolder(msg);

                Console.WriteLine($"Пришло сообщение с файлом {msg.Document.FileName}");
                await UploadFile(msg);

                state = ReceivingState.WaitingCommand;
                await client.SendTextMessageAsync(msg.Chat.Id, "Файл сохранен.", replyMarkup: null);
            }

            // Запрос города для получения погоды
            if (msg.Text == "/weather")
            {
                state = ReceivingState.WaitingCity;

                await client.SendTextMessageAsync(msg.Chat.Id, "Введи название города:", replyMarkup: null);
            }

            // Отображение списка файлов и запрос имени файла для скачивания
            if (msg.Text == "/files")
            {
                state = ReceivingState.WaitingFileName;
                CheckUploadFolder(msg);

                await GetFilesList(msg);
            }

            if (msg.Text == "/cancel")
            {
                state = ReceivingState.WaitingCommand;
            }
        }
        /// <summary>
        /// Загрузка файла в директорию
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private static async Task UploadFile(Message msg)
        {
            var file = await client.GetFileAsync(msg.Document.FileId);
            var fileName = msg.Document.FileName;
            FileStream fs = new FileStream($"{FileDir}/{msg.Chat.Id}/{fileName}", FileMode.Create);
            await client.DownloadFileAsync(file.FilePath, fs);
            fs.Close();
            await fs.DisposeAsync();
        }
        /// <summary>
        /// Проверка наличия директория пользователя для сохранения файла
        /// </summary>
        /// <param name="msg"></param>
        private static void CheckUploadFolder(Message msg)
        {
            if (!Directory.Exists($"{FileDir}/{msg.Chat.Id}"))
            {
                Directory.CreateDirectory($"{FileDir}/{msg.Chat.Id}");
            }
        }

        /// <summary>
        /// Возвращает список файлов
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private static async Task GetFilesList(Message msg)
        {
            var userId = msg.Chat.Id;
            string[] files = Directory.GetFiles($"{FileDir}/{userId}");

            StringBuilder sb = new StringBuilder();

            foreach (var file in files)
            {
                sb.Append($"{file.Substring(21)}\n");
            }

            var listFile = sb.ToString();

            await client.SendTextMessageAsync(msg.Chat.Id,
                listFile + "\n\nДля скачивания файла введи его имя, для отмены /cancel:\n",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html);
        }

        /// <summary>
        /// Возвращает погоду
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private static async Task SendWeather(Message msg)
        {
            state = ReceivingState.WaitingCommand;

            const string weatherApiKey = "0ef64ffa4b4a21ed287172f79e03b1d4"; // токен для OpenWeatherMap
            var city = msg.Text;
            HttpClient httpClient = new HttpClient();

            var currentUrl = $"http://api.openweathermap.org/data/2.5/weather?q={msg.Text}" +
                             $"&mode=json&units=metric&APPID={weatherApiKey}";

            try
            {
                var weatherContent = await httpClient.GetStringAsync(currentUrl);
                var tempWeather = JsonConvert.DeserializeObject<Root>(weatherContent, new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented
                });


                var textWeather = $"Погода в городе: {tempWeather.Name}\n" +
                                  $"{Convert.ToInt32(tempWeather.Main.Temp).ToString()}°C";

                await client.SendTextMessageAsync(msg.Chat.Id, textWeather);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                await client.SendTextMessageAsync(msg.Chat.Id, "Город введен некорректно!");
            }


        }

        /// <summary>
        /// Кнопочки
        /// </summary>
        /// <returns></returns>
        private static IReplyMarkup GetButtons()
        {
            return new ReplyKeyboardMarkup
            {
                Keyboard = new List<List<KeyboardButton>>
                {
                    new List<KeyboardButton>
                    {
                        new KeyboardButton {Text = "/weather"},
                        new KeyboardButton {Text = "/files"}
                    }
                }
            };
        }
    }
}
