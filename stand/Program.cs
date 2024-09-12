using System;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DiscordBot
{
    class Program
    {
        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;
        private IDbConnection _dbConnection;

        static void Main(string[] args) => new Program().RunBotAsync().GetAwaiter().GetResult();

        public async Task RunBotAsync()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged |
                                 GatewayIntents.GuildMembers
            };

            _client = new DiscordSocketClient(config);
            _commands = new CommandService();

            // Настройка подключения к базе данных
            _dbConnection = new NpgsqlConnection("Host=93.158.195.14;Username=lka;Password=1548;Database=distant");

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .AddSingleton(_dbConnection) 
                .BuildServiceProvider();

            string botToken = "";

            _client.Log += Log;

            await RegisterCommandsAsync();

            await _client.LoginAsync(TokenType.Bot, botToken);
            await _client.StartAsync();

            Console.WriteLine("Бот запущен...");
            await Task.Delay(-1);
        }

        private Task Log(LogMessage arg)
        {
            Console.WriteLine(arg);
            return Task.CompletedTask;
        }

        public async Task RegisterCommandsAsync()
        {
            _client.MessageReceived += HandleCommandAsync;
            await _commands.AddModulesAsync(System.Reflection.Assembly.GetEntryAssembly(), _services);
        }

        private async Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;
            if (message == null || message.Author.IsBot) return;

            var context = new SocketCommandContext(_client, message);

            int argPos = 0;
            if (message.HasStringPrefix("!", ref argPos))
            {
                var result = await _commands.ExecuteAsync(context, argPos, _services);
                if (!result.IsSuccess && result.Error != CommandError.UnknownCommand)
                {
                    Console.WriteLine(result.ErrorReason);
                }
            }
        }
    }

    public class CommandModule : ModuleBase<SocketCommandContext>
    {
        private static readonly Dictionary<ulong, DateTime> _userReservedDate = new Dictionary<ulong, DateTime>();
        private static readonly Dictionary<ulong, string> _userReservedTopic = new Dictionary<ulong, string>();
        private static readonly Dictionary<ulong, bool> _userStandStatus = new Dictionary<ulong, bool>();

        private readonly DiscordSocketClient _client;

        private const ulong LogChannelId = 1280464168285245531; 
        private readonly IDbConnection _dbConnection;

        public CommandModule(IDbConnection dbConnection, DiscordSocketClient client)
        {
            _dbConnection = dbConnection;
            _client = client;
        }

        [Command("stand")]
        public async Task StandAsync()
        {
            var user = Context.User as SocketGuildUser;
            if (user.Roles.Any(r => r.Name == "verificate"))
            {
                var userId = await _dbConnection.QuerySingleOrDefaultAsync<int>(
            "SELECT User_ID FROM \"User\" WHERE User_Discord_Tag = @UserTag",
            new { UserTag = user.Username });

                if (userId == 0)
                {
                    return;
                }

                var balance = await _dbConnection.QuerySingleOrDefaultAsync<int>(
                    "SELECT Account_Score FROM \"Bank\" WHERE ID_User = @UserId",
                    new { UserId = userId });

                if (balance < 10)
                {
                    await Context.Channel.SendMessageAsync($"{user.Mention}, недостаточно средств для резервирования трибуны. Необходимо 10 монет.");
                    return;
                }

                await Context.Channel.SendMessageAsync($"{user.Mention}, хочешь занять место на трибуне? используй команды: `!reserve date [дата] в формате dd.MM.yyyy`, `!reserve time [время] в формате HH:mm` и `!reserve topic [тема]`.");
                _userStandStatus[user.Id] = true;
            }
        }

        [Command("reserve")]
        public async Task ReserveAsync(string type, [Remainder] string value = null)
        {
            var user = Context.User as SocketGuildUser;
            if (user.Roles.Any(r => r.Name == "verificate"))
            {
                var userId = await _dbConnection.QuerySingleOrDefaultAsync<int>(
            "SELECT User_ID FROM \"User\" WHERE User_Discord_Tag = @UserTag",
            new { UserTag = user.Username });
                var bankId = await _dbConnection.QuerySingleOrDefaultAsync<int>(
                "SELECT Bank_ID FROM \"Bank\" WHERE ID_User = @UserId",
                new { UserId = userId });

                if (!_userStandStatus.TryGetValue(user.Id, out var isStanding) || !isStanding)
                {
                    await Context.Channel.SendMessageAsync($"{user.Mention}, Сначала используй команду `!stand`.");
                    return;
                }

                if (string.IsNullOrEmpty(value))
                {
                    await Context.Channel.SendMessageAsync($"{user.Mention}, пожалуйста, укажите значение для {type}. Используйте команды `!reserve date [дата]`, `!reserve time [время]` или `!reserve topic [тема]`.");
                    return;
                }

                if (type.Equals("date", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTime.TryParseExact(value, "dd.MM.yyyy", null, DateTimeStyles.None, out var date))
                    {
                        var now = DateTime.Now.Date;
                        if (date < now.AddDays(7) || date > now.AddMonths(1))
                        {
                            await Context.Channel.SendMessageAsync($"{user.Mention}, вы можете резервировать дату минимум через 7 дней и максимум через месяц.");
                            return;
                        }

                        var existingReservation = await _dbConnection.QuerySingleOrDefaultAsync<int?>(
                            "SELECT COUNT(*) FROM \"Reserv\" WHERE Reserv_Date = @Date", new { Date = date });

                        if (existingReservation > 0)
                        {
                            await Context.Channel.SendMessageAsync($"{user.Mention}, эта дата занята, выбери другую");
                            return;
                        }

                        _userReservedDate[user.Id] = date;
                        await Context.Channel.SendMessageAsync($"{user.Mention}, ты выбрал дату {date:dd.MM.yyyy}. Введите `!reserve time [время]` для времени.");
                    }
                    else
                    {
                        await Context.Channel.SendMessageAsync($"{user.Mention}, неверный формат даты. Используй формат `dd.MM.yyyy`.");
                    }
                }
                else if (type.Equals("time", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTime.TryParseExact(value, "HH:mm", null, DateTimeStyles.None, out var time))
                    {
                        if (_userReservedDate.TryGetValue(user.Id, out var reservedDate))
                        {
                            if (time.Hour < 16 || time.Hour > 20 || (time.Hour == 20 && time.Minute > 0))
                            {
                                await Context.Channel.SendMessageAsync($"{user.Mention}, время резервирования должно быть с 16:00 до 20:00.");
                                return;
                            }

                            var reservedDateTime = new DateTime(reservedDate.Year, reservedDate.Month, reservedDate.Day, time.Hour, time.Minute, 0);
                            _userReservedDate[user.Id] = reservedDateTime;
                            await Context.Channel.SendMessageAsync($"{user.Mention}, ты выбрал время {time:HH:mm}. Введите `!reserve topic [тема]` для темы.");
                        }
                        else
                        {
                            await Context.Channel.SendMessageAsync($"{user.Mention}, сначала укажи дату. Используй команду `!reserve date [дата]`.");
                        }
                    }
                    else
                    {
                        await Context.Channel.SendMessageAsync($"{user.Mention}, неверный формат времени. Используй формат `HH:mm`.");
                    }
                }
                else if (type.Equals("topic", StringComparison.OrdinalIgnoreCase))
                {
                    if (_userReservedDate.TryGetValue(user.Id, out var reservedDateTime))
                    {
                        await _dbConnection.ExecuteAsync(
                            "INSERT INTO \"Reserv\" (ID_User, Reserv_Date, Reserv_Time, Reserv_Topic) " +
                            "VALUES ((SELECT User_ID FROM \"User\" WHERE User_Discord_Tag = @Tag), @Date, @Time, @Topic)",
                            new { Tag = user.Username, Date = reservedDateTime.Date, Time = reservedDateTime.TimeOfDay, Topic = value });

                        _userStandStatus[user.Id] = false;
                        _userReservedDate.Remove(user.Id);

                        var announcement = $"{user.Mention} будет проводить выступление на трибуне {reservedDateTime:dd.MM.yyyy} в {reservedDateTime:HH:mm} по теме: {value}.";

                        var excludedChannels = new List<ulong> { 1280464491783520340, 1280458062221807686, 1280460997798264925, 1280461427571822744, 1280462234908098560, 1280462048186204193, 1280462101768568872, 1280462131598462987, 1280462738681761874, 1280463222662631455, 1280463687089651776, 1280460613776179250, 1280464015012794383, 1280464051490783314, 1280464168285245531, 1280464239147880459 }; // ID каналов

                        foreach (var channel in Context.Guild.TextChannels)
                        {
                            if (excludedChannels.Contains(channel.Id))
                            {
                                continue; 
                            }

                            try
                            {
                                await channel.SendMessageAsync(announcement);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Не удалось отправить сообщение в канал {channel.Name}: {ex.Message}");
                            }
                        }

                        var logId = await _dbConnection.QuerySingleAsync<int>(
                "INSERT INTO \"Log\" (ID_Operation, ID_Item, ID_User, ID_Bank, Transaction_Result) " +
                "VALUES (2, NULL, @UserId, @BankId, @Amount) " +
                "RETURNING log_id;",
                new { UserId = userId, BankId = bankId, Amount = $"-10" });

                        await _dbConnection.ExecuteAsync("UPDATE \"Bank\" SET Account_Score = Account_Score - 10 WHERE Bank_ID = @BankId",
                                              new { BankId = bankId });

                        var transactionId = await _dbConnection.QuerySingleOrDefaultAsync<string>(
                            "SELECT Transaction FROM \"Log\" WHERE Log_id = @LogId",
                            new { LogId = logId });

                        await ReplyAsync($"Вы зарезервировали трибуну за 10 монет. Номер транзакции: {transactionId}");

                        var logChannel = _client.GetChannel(LogChannelId) as ITextChannel;
                        if (logChannel != null)
                        {
                            await logChannel.SendMessageAsync($"{user.Mention} зарезервировал трибуну за 10 монет. Номер транзакции: {transactionId}");
                        }

                        var role = Context.Guild.Roles.FirstOrDefault(r => r.Name == "tribune");
                        if (role != null)
                        {
                            var delay = reservedDateTime - DateTime.Now;
                            if (delay.TotalMilliseconds > 0)
                            {
                                await Task.Delay(delay);
                                await (user as IGuildUser).AddRoleAsync(role);

                                await Task.Delay(TimeSpan.FromHours(1));
                                await (user as IGuildUser).RemoveRoleAsync(role);
                            }
                        }
                    }
                    else
                    {
                        await Context.Channel.SendMessageAsync($"{user.Mention}, сначала укажите дату и время. Используйте команды `!reserve date [дата]` и `!reserve time [время]`.");
                    }
                }
            }
        }
    }
}

