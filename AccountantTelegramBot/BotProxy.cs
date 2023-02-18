using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;

namespace AccountantTelegramBot
{
    internal class BotProxy
    {
        const string API_TOKEN = "5722607815:AAEOtv6uqaHLEyDQBgnh715vMukIzCsaS_0";

        static ITelegramBotClient bot = new TelegramBotClient(API_TOKEN);
        static bool _isStarted = false;
        static Dictionary<long, List<CostEntity>> _messagesWithSums = new Dictionary<long, List<CostEntity>>();
        static string _userNameOfMaxSum = default!;

        public static void Start()
        {
            Console.WriteLine("Started bot " + bot.GetMeAsync().Result.FirstName);

            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { }, // receive all update types
            };

            bot.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken
            );
            Console.ReadLine();
        }

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(update));
            Message? message = null;
            if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
                message = update.Message;
            else if (update.Type == Telegram.Bot.Types.Enums.UpdateType.EditedMessage)
                message = update.EditedMessage;

            if (message != null)
            {
                string messageText = message?.Text?.ToLower();
                if (message?.Text?.ToLower() == "/start")
                {
                    await OnStart(botClient, message.Chat);
                    return;
                }
                else if (_isStarted)
                {
                    if (message?.Text?.ToLower() == "/calculate")
                    {
                        await OnCalculate(botClient, message.Chat.Id);
                    }
                    else if (message?.Text?.ToLower() == "/reset")
                    {
                        await OnReset(botClient, message);
                    }
                    else
                    {
                        var words = messageText.Split(' ');
                        bool wasAdded = false;

                        List<CostEntity> sums;
                        if (!_messagesWithSums.TryGetValue(message.Chat.Id, out sums))
                            sums = new List<CostEntity>();

                        for (int i = 0; i < words.Length; i++)
                        {
                            if (words[i].Contains("грн"))
                            {


                                if (words[i] == "грн")
                                {
                                    if (i > 0)
                                    {
                                        string expectedSumStr = words[i - 1];
                                        if (TryAddSum(sums, expectedSumStr, message.From?.Username))
                                            wasAdded = true;
                                    }
                                }
                                else
                                {
                                    var expectedSumStr = words[i].Remove(words[i].Length - 3, 3);
                                    if (TryAddSum(sums, expectedSumStr, message.From?.Username))
                                        wasAdded = true;
                                }
                            }
                        }

                        if (wasAdded && !_messagesWithSums.ContainsKey(message.Chat.Id))
                        {
                            _messagesWithSums.Add(message.Chat.Id, sums);
                        }
                    }

                    //await botClient.SendTextMessageAsync(message?.Chat, "Русні пизда!");
                }
            }
        }

        static bool TryAddSum(List<CostEntity> sums, string expectedSumStr, string username)
        {            
            if (double.TryParse(expectedSumStr, out double sum))
            {
                sums.Add(new CostEntity
                {
                    UserName = username,
                    Sum = sum
                });

                Console.WriteLine($"User {username} has added {expectedSumStr} UAH to chat");
                return true;
            }

            return false;
        }

        static async Task OnReset(ITelegramBotClient botClient, Message message)
        {
            if (!string.IsNullOrEmpty(_userNameOfMaxSum) && _userNameOfMaxSum != message.From.Username)
            {
                await botClient.SendTextMessageAsync(message.Chat, "Nice try! Користувач, якому вині гроші повинен обнулити лічильник :)");
                return;
            }

            _userNameOfMaxSum = string.Empty;
            _messagesWithSums.Clear();
            await botClient.SendTextMessageAsync(message.Chat, "Раз, два, три - гра заново почалась!");
        }

        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(exception));
        }

        static async Task OnStart(ITelegramBotClient botClient, ChatId chat)
        {
            _isStarted = true;
            await botClient.SendTextMessageAsync(chat, "Буду гроші рахував");
        }

        static async Task OnCalculate(ITelegramBotClient botClient, ChatId chat)
        {
            if (_messagesWithSums.TryGetValue(chat.Identifier.Value, out List<CostEntity> costEntities))
            {
                Dictionary<string, UserCostSum> sumsMap = new Dictionary<string, UserCostSum>();
                foreach (var sum in costEntities)
                {
                    UserCostSum userCostSum;
                    if (!sumsMap.ContainsKey(sum.UserName))
                    {
                        userCostSum = new UserCostSum()
                        {
                            UserName = sum.UserName,
                        };
                        sumsMap.Add(sum.UserName, userCostSum);
                    }
                    else
                        userCostSum = sumsMap[sum.UserName];

                    userCostSum.Sum += sum.Sum;
                }

                StringBuilder sb = new StringBuilder();
                foreach (var userCostSum in sumsMap.Values)
                {
                    sb.Append($"{userCostSum.UserName} оплатив всього на {userCostSum.Sum} грн\n");
                }

                var maxCostSum = sumsMap.Values.MaxBy(s => s.Sum);
                _userNameOfMaxSum = maxCostSum.UserName;
                foreach (var userCostSum in sumsMap.Values)
                {
                    if (maxCostSum.UserName == userCostSum.UserName)
                        continue;

                    sb.Append($"Користувач {userCostSum.UserName} винен користувачу {maxCostSum.UserName} {maxCostSum.Sum - userCostSum.Sum} грн\n");
                }
                await botClient.SendTextMessageAsync(chat, sb.ToString());
            }
            else await botClient.SendTextMessageAsync(chat, "Ніхто нікому нічого не винен");
        }
    }
}
