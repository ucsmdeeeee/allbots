using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot
{
    class Program
    {
        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;

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
            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .BuildServiceProvider();

            string botToken = ""; 

            _client.Log += Log;

            await RegisterCommandsAsync();

            await _client.LoginAsync(TokenType.Bot, botToken);
            await _client.StartAsync();

            Console.WriteLine("Bot is running...");
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
        [Command("sendquests")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SendQuestsAsync([Remainder] string message)
        {
            var user = Context.User as SocketGuildUser;
            var adminRole = Context.Guild.Roles.FirstOrDefault(r => r.Name == "Admin");

            if (adminRole == null)
            {
                await ReplyAsync("Роль Admin не найдена на этом сервере.");
                return;
            }

            if (user.Roles.Contains(adminRole))
            {
                var excludedChannels = new List<ulong> { 1280464491783520340, 1280458062221807686, 1280460997798264925, 1280461427571822744, 1280462234908098560, 1280462048186204193, 1280462101768568872, 1280462131598462987, 1280462738681761874, 1280463222662631455, 1280463687089651776, 1280460613776179250, 1280464015012794383, 1280464051490783314, 1280464168285245531, 1280464239147880459 }; // ID каналов

                foreach (var channel in Context.Guild.TextChannels)
                {
                    if (excludedChannels.Contains(channel.Id))
                    {
                        continue;
                    }

                    try
                    {
                        await channel.SendMessageAsync(message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Не удалось отправить сообщение в канал {channel.Name}: {ex.Message}");
                    }
                }
                await ReplyAsync("Квест отправлен во все текстовые каналы!");
            }
        }
    }
}
