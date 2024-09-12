using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace MBTIDiscordBot
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
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers
            };

            _client = new DiscordSocketClient(config);
            _commands = new CommandService();
            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .BuildServiceProvider();

            string botToken = ""; 

            _client.Log += Log;
            _client.UserJoined += UserJoined;
            _client.GuildMemberUpdated += GuildMemberUpdatedAsync;

            await RegisterCommandsAsync();

            await _client.LoginAsync(TokenType.Bot, botToken);
            await _client.StartAsync();

            Console.WriteLine("Bot is running...");
            await Task.Delay(-1);
        }

        private async Task UserJoined(SocketGuildUser user)
        {
            Console.WriteLine($"User joined: {user.Username}#{user.Discriminator}");

            try
            {
                var guild = user.Guild;
                var adminRole = guild.Roles.FirstOrDefault(r => r.Permissions.Administrator);

                var channel = await guild.CreateTextChannelAsync(user.Username, x =>
                {
                    x.PermissionOverwrites = new Overwrite[]
                    {
                new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
                new Overwrite(user.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow)),
                new Overwrite(adminRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Allow))
                    };
                    x.SlowModeInterval = 10;
                });

                if (channel != null)
                {
                    Console.WriteLine($"Created private channel for {user.Username}");

                    await channel.SendMessageAsync("Приветствую в твоем временном канале, чтобы начать пользоваться сервером пройди авторизацию. Напиши сюда !authorization");
                }
                else
                {
                    Console.WriteLine("Failed to create private channel.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating private channel: {ex.Message}");
            }
        }


        private async Task GuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
        {
            Console.WriteLine($"Guild member updated: {after.Username}");

            try
            {
                var guild = after.Guild;
                if (guild == null)
                {
                    Console.WriteLine("Guild is null.");
                    return;
                }

                var notVerificateRole = guild.Roles.FirstOrDefault(r => r.Name == "notverificate");
                var verificateChannel = guild.TextChannels.FirstOrDefault(c => c.Name == "verificate");

                var beforeUser = await before.GetOrDownloadAsync();

                if (beforeUser.Roles.Contains(notVerificateRole) && !after.Roles.Contains(notVerificateRole))
                {
                    var privateChannel = guild.TextChannels.FirstOrDefault(c => c.Name == after.Username);

                    if (privateChannel != null)
                    {
                        await privateChannel.DeleteAsync();
                        Console.WriteLine($"Deleted private channel for {after.Username}");
                    }

                    if (verificateChannel != null)
                    {
                        await verificateChannel.AddPermissionOverwriteAsync(after, new OverwritePermissions(viewChannel: PermValue.Allow));
                        Console.WriteLine($"Added {after.Username} to verificate channel");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GuildMemberUpdatedAsync: {ex.Message}");
            }
        }

        private Task Log(LogMessage arg)
        {
            Console.WriteLine(arg);
            return Task.CompletedTask;
        }

        public async Task RegisterCommandsAsync()
        {
            _client.MessageReceived += HandleCommandAsync;
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
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

    public class UserTestState
    {
        public string Gender { get; set; }
        public List<char> Answers { get; } = new List<char>();
        public int CurrentQuestionIndex { get; set; } = 0;
    }
}
