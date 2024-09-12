using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Dapper;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Data;
using System.Reflection;

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
                             GatewayIntents.GuildMembers |
                             GatewayIntents.GuildMessages
        };

        _client = new DiscordSocketClient(config);
        _commands = new CommandService();

        _dbConnection = new NpgsqlConnection("Host=93.158.195.14;Username=lka;Password=1548;Database=distant");

        _services = new ServiceCollection()
            .AddSingleton(_client)
            .AddSingleton(_commands)
            .AddSingleton(_dbConnection) 
            .BuildServiceProvider();

        string botToken = ""; 

        _client.Log += Log;
        _client.MessageReceived += HandleCommandAsync;

        await RegisterCommandsAsync();

        Console.WriteLine("Вход в систему...");
        await _client.LoginAsync(TokenType.Bot, botToken);
        Console.WriteLine("Запуск бота...");
        await _client.StartAsync();

        Console.WriteLine("Бот запущен...");
        await Task.Delay(-1);
    }

    private Task Log(LogMessage log)
    {
        Console.WriteLine($"{DateTime.Now}: {log.Severity} - {log.Source}: {log.Message}");
        return Task.CompletedTask;
    }

    private async Task HandleCommandAsync(SocketMessage arg)
    {
        var message = arg as SocketUserMessage;
        var context = new SocketCommandContext(_client, message);

        if (message.Author.IsBot) return;

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

    public async Task RegisterCommandsAsync()
    {
        Console.WriteLine("Регистрация команд...");
        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        Console.WriteLine("Команды зарегистрированы.");
    }
}

public class MyCommands : ModuleBase<SocketCommandContext>
{
    private readonly IDbConnection _dbConnection;
    private readonly DiscordSocketClient _client;

    private const ulong LogChannelId = 1280464168285245531; 

    public MyCommands(IDbConnection dbConnection, DiscordSocketClient client)
    {
        _dbConnection = dbConnection;
        _client = client;
    }

    [Command("addcoin")]
    public async Task AddCoinAsync(string userTag, int amount)
    {
        var user = Context.User as SocketGuildUser;
        if (user.Roles.Any(r => r.Name == "Admin"))
        {

            var userId = await _dbConnection.QuerySingleOrDefaultAsync<int>(
                "SELECT User_ID FROM \"User\" WHERE User_Discord_Tag = @UserTag",
                new { UserTag = userTag });

            if (userId == 0)
            {
                await ReplyAsync("Пользователь не зарегистрирован в базе данных.");
                return;
            }


            var bankId = await _dbConnection.QuerySingleOrDefaultAsync<int>(
                "SELECT Bank_ID FROM \"Bank\" WHERE ID_User = @UserId",
                new { UserId = userId });

            if (bankId == 0)
            {
                await ReplyAsync("Банковский счет не найден.");
                return;
            }

            var logId = await _dbConnection.ExecuteAsync(
                "INSERT INTO \"Log\" (ID_Operation, ID_Item, ID_User, ID_Bank, Transaction_Result) VALUES " +
                "(1, NULL, @UserId, @BankId, @Amount)",
                new { UserId = userId, BankId = bankId, Amount = amount });
            await _dbConnection.ExecuteAsync("UPDATE \"Bank\" SET Account_Score = Account_Score + @ItemPrice WHERE Bank_ID = @BankId",
                                  new { ItemPrice = amount, BankId = bankId });
            await ReplyAsync($"Добавлено {amount} монет пользователю {userTag}.");

            var transactionId = await _dbConnection.QuerySingleOrDefaultAsync<string>(
                "SELECT Transaction FROM \"Log\" WHERE Log_id = @LogId",
                new { LogId = logId });


            var logChannel = _client.GetChannel(LogChannelId) as ITextChannel;
            if (logChannel != null)
            {
                await logChannel.SendMessageAsync($"{user.Mention} начислено {amount} монет. Номер транзакции: {transactionId}");
            }
        }
        else
        {
            await ReplyAsync("У вас нет прав для выполнения этой команды.");
        }
    }

    [Command("checkbalance")]
    public async Task CheckBalanceAsync()
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

            await ReplyAsync($"Ваш баланс составляет {balance} монет.");
        }
    }
}
