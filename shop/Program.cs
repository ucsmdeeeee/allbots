using Dapper;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

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
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers
        };

        _client = new DiscordSocketClient(config);
        _commands = new CommandService();

        _dbConnection = new NpgsqlConnection("Host=93.158.195.14;Username=lka;Password=1548;Database=distant");

        _services = new ServiceCollection()
            .AddSingleton(_client)
            .AddSingleton(_commands)
            .AddSingleton<IDbConnection>(_dbConnection)
            .BuildServiceProvider();

        string botToken = ""; 

        _client.Log += Log;
        _client.Ready += Ready;
        _client.MessageReceived += HandleCommandAsync;

        await RegisterCommandsAsync();

        await _client.LoginAsync(TokenType.Bot, botToken);
        await _client.StartAsync();

        Console.WriteLine("Bot is running...");
        await Task.Delay(-1);
    }

    private Task Log(LogMessage log)
    {
        Console.WriteLine(log);
        return Task.CompletedTask;
    }

    private Task Ready()
    {
        Console.WriteLine("Bot is connected!");
        return Task.CompletedTask;
    }

    private async Task HandleCommandAsync(SocketMessage arg)
    {
        var message = arg as SocketUserMessage;
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

    private Task RegisterCommandsAsync()
    {
        _commands.AddModuleAsync<ShopModule>(_services);
        _commands.AddModuleAsync<BuyModule>(_services);
        return Task.CompletedTask;
    }
}


public class ShopModule : ModuleBase<SocketCommandContext>
{
    private readonly IDbConnection _dbConnection;

    public ShopModule(IDbConnection dbConnection)
    {
        _dbConnection = dbConnection;
    }

    [Command("shop")]
    public async Task Shop()
    {
        var items = _dbConnection.Query<Item>("SELECT * FROM \"Item\"").ToList();

        if (!items.Any())
        {
            await ReplyAsync("Товары не найдены.");
            return;
        }

        var response = string.Join("\n", items.Select(item =>
            $"Название: {item.Item_Title}, Цена: {item.Item_Prise}, Фото: {item.Item_Photo ?? "Нет фото"}"));

        await ReplyAsync(response);
        await ReplyAsync("Ты можешь купить товар, используя команду, !buy название товара");
    }
}

public class Item
{
    public int Item_ID { get; set; }
    public string Item_Title { get; set; }
    public int Item_Prise { get; set; }
    public string Item_Photo { get; set; }
}


public class BuyModule : ModuleBase<SocketCommandContext>
{
    private readonly IDbConnection _dbConnection;
    private readonly DiscordSocketClient _client;

    private const ulong LogChannelId = 1280464168285245531; 

    public BuyModule(IDbConnection dbConnection, DiscordSocketClient client)
    {
        _dbConnection = dbConnection;
        _client = client;
    }

    [Command("buy")]
    public async Task Buy(string itemTitle)
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

            // Найти банковский счет пользователя
            var balance = await _dbConnection.QuerySingleOrDefaultAsync<int>(
                "SELECT Account_Score FROM \"Bank\" WHERE ID_User = @UserId",
                new { UserId = userId });
            var bankId = await _dbConnection.QuerySingleOrDefaultAsync<int>(
                "SELECT Bank_ID FROM \"Bank\" WHERE ID_User = @UserId",
                new { UserId = userId });

            
            var Item_Title = await _dbConnection.QuerySingleOrDefaultAsync<string>(
                "SELECT Item_Title FROM \"Item\" WHERE Item_Title = @ItemTitle",
                new { ItemTitle = itemTitle });
            var Item_Prise = await _dbConnection.QuerySingleOrDefaultAsync<int>(
                "SELECT Item_Prise FROM \"Item\" WHERE Item_Title = @ItemTitle",
                new { ItemTitle = itemTitle });
            var Item_ID = await _dbConnection.QuerySingleOrDefaultAsync<int>(
                "SELECT Item_ID FROM \"Item\" WHERE Item_Title = @ItemTitle",
                new { ItemTitle = itemTitle });
            if (Item_Title == null)
            {
                return;
            }

            if (balance < Item_Prise)
            {
                await ReplyAsync("Недостаточно средств.");
                return;
            }

            var logId = await _dbConnection.QuerySingleAsync<int>(
                "INSERT INTO \"Log\" (ID_Operation, ID_Item, ID_User, ID_Bank, Transaction_Result) " +
                "VALUES (3, @ItemId, @UserId, @BankId, @Amount) " +
                "RETURNING log_id;",
                new { ItemId = Item_ID, UserId = userId, BankId = bankId, Amount = $"-{Item_Prise}" });

            await _dbConnection.ExecuteAsync("UPDATE \"Bank\" SET Account_Score = Account_Score - @ItemPrice WHERE Bank_ID = @BankId",
                                  new { ItemPrice = Item_Prise, BankId = bankId });

            var transactionId = await _dbConnection.QuerySingleOrDefaultAsync<string>(
                "SELECT Transaction FROM \"Log\" WHERE Log_id = @LogId",
                new { LogId = logId });

            await ReplyAsync($"Вы купили {Item_Title} за {Item_Prise} монет. Номер транзакции: {transactionId}");

            var logChannel = _client.GetChannel(LogChannelId) as ITextChannel;
            if (logChannel != null)
            {
                await logChannel.SendMessageAsync($"{user.Mention} купил {Item_Title} за {Item_Prise} монет. Номер транзакции: {transactionId}");
            }
        }
    }
}
