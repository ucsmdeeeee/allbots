using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Npgsql;
using Dapper;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;

namespace MBTIDiscordBot
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

            _dbConnection = new NpgsqlConnection("Host=93.158.195.14;Username=lka;Password=1548;Database=distant");

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .AddSingleton(_dbConnection) 
                .BuildServiceProvider();

            string botToken = "";

            _client.Log += Log;
            _client.UserJoined += UserJoined;

            await RegisterCommandsAsync();

            await _client.LoginAsync(TokenType.Bot, botToken);
            await _client.StartAsync();

            Console.WriteLine("Bot is running...");
            await Task.Delay(-1);
        }

        private async Task UserJoined(SocketGuildUser user)
        {
            Console.WriteLine($"User joined: {user.Username}#{user.Discriminator}");

            var role = user.Guild.Roles.FirstOrDefault(r => r.Name == "notverificate");
            if (role != null)
            {
                try
                {
                    await user.AddRoleAsync(role);
                    Console.WriteLine($"Role 'notverificate' added to user: {user.Username}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error adding role: {ex.Message}");
                }

            }
            else
            {
                Console.WriteLine("Role 'notverificate' not found.");
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

    public class AuthorizationModule : ModuleBase<SocketCommandContext>
    {
        private readonly IDbConnection _dbConnection;
        private static readonly Dictionary<ulong, AuthorizationState> AuthorizationStates = new();

        public AuthorizationModule(IDbConnection dbConnection)
        {
            _dbConnection = dbConnection;
        }

        private async Task<bool> HasAuthorizeRole(SocketGuildUser user)
        {
            return user.Roles.Any(r => r.Name == "authorize");
        }

        private async Task EnsureNotAuthorizeRole(SocketGuildUser user)
        {
            if (await HasAuthorizeRole(user))
            {
                await ReplyAsync("Ты уже авторизован. Используй команду тестирования !starttest");
            }
        }

        [Command("authorization")]
        public async Task AuthorizationAsync()
        {
            var user = Context.User as SocketGuildUser;

            if (await HasAuthorizeRole(user))
            {
                await ReplyAsync("Ты уже авторизован. Используй команду тестирования !starttest");
                return;
            }

            var userId = Context.User.Id;

            if (AuthorizationStates.ContainsKey(userId))
            {
                await ReplyAsync("Вы уже начали процесс авторизации. Пожалуйста, используйте команду !send для продолжения.");
                return;
            }

            var state = new AuthorizationState { Step = "Email" };
            AuthorizationStates[userId] = state;
            await ReplyAsync("Используй команду !send твоя почта, чтоб ввести почту!:");
        }

        [Command("send")]
        public async Task SendAsync(string input = null)
        {
            var user = Context.User as SocketGuildUser;

            if (await HasAuthorizeRole(user))
            {
                await ReplyAsync("Ты уже авторизован. Используй команду тестирования !starttest");
                return;
            }

            var userId = Context.User.Id;
            if (!AuthorizationStates.TryGetValue(userId, out var state))
            {
                await ReplyAsync("Вы не начали процесс авторизации. Используйте команду !authorization для начала.");
                return;
            }

            if (state.Step == "Email")
            {
                if (!input.Contains("@st.ithub.ru"))
                {
                    await ReplyAsync("Введи корректный email!");
                    return;
                }

                state.Email = input;
                await ReplyAsync("Используй команду !send твой пароль, чтоб ввести пароль!:");
                state.Step = "Password";
            }
            else if (state.Step == "Password")
            {
                state.Password = input;

                var userRecord = await _dbConnection.QueryFirstOrDefaultAsync<UserRecord>(
                    "SELECT * FROM \"User\" WHERE user_email = @Email AND user_password = @Password",
                    new { Email = state.Email, Password = state.Password }
                );

                if (userRecord == null)
                {
                    await ReplyAsync("Неверные данные. Используй команду !authorization снова");
                    AuthorizationStates.Remove(userId);
                    return;
                }

                await _dbConnection.ExecuteAsync(
                    "UPDATE \"User\" SET user_discord_tag = @Tag, user_ver = 'yes' WHERE user_id = @UserID",
                    new { Tag = Context.User.Username, UserID = userRecord.User_ID }
                );

                var kaf = await _dbConnection.QuerySingleOrDefaultAsync<string>(
                "SELECT User_Kaf FROM \"User\" WHERE user_email = @Email AND user_password = @Password",
                new { Email = state.Email, Password = state.Password });

                var kafRole = user.Guild.Roles.FirstOrDefault(r => r.Name == kaf);               
                if (kafRole != null)
                {
                    try
                    {
                        await user.AddRoleAsync(kafRole);
                        Console.WriteLine($"Role 'kaf' added to user: {user.Username}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error adding role: {ex.Message}");
                    }
                }
                var authorizeRole = user.Guild.Roles.FirstOrDefault(r => r.Name == "authorize");
                if (authorizeRole != null)
                {
                    try
                    {
                        await user.AddRoleAsync(authorizeRole);
                        Console.WriteLine($"Role 'authorize' added to user: {user.Username}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error adding role: {ex.Message}");
                    }
                }

                
                await user.ModifyAsync(properties => properties.Nickname = userRecord.User_FIO);

                await ReplyAsync("Ты успешно авторизован. Теперь ты можете использовать команду !starttest.");

                AuthorizationStates.Remove(userId);
            }
        }

        private class AuthorizationState
        {
            public string Step { get; set; }
            public string FIO { get; set; }
            public string Email { get; set; }
            public string Password { get; set; }
        }

        private class UserRecord
        {
            public int User_ID { get; set; }
            public string User_FIO { get; set; }
            public string User_Email { get; set; }
            public string User_Password { get; set; }
            public string User_Discord_Tag { get; set; }
            public string User_Ver { get; set; }
        }
    }







    public class TestModule : ModuleBase<SocketCommandContext>
    {
        

        private static readonly List<Question> Questions = new List<Question>
    {
        new Question("Обычно Вы:", new[] { "А) общительны", "Б) довольно сдержанны и спокойны \nЧтобы дать ответ на вопрос используй команду !answer а, б или в!" }),
                new Question("Если бы Вы были преподавателем, какой курс Вы бы предпочли:", new[] { "А) построенный на изложении фактов", "Б) включающий в себя изложение теорий" }),
                new Question("Вы чаще позволяете:", new[] { "А) своему сердцу управлять разумом", "Б) своему разуму управлять сердцем" }),
                new Question("Когда Вы уходите куда-то на весь день, Вы:", new[] { "А) планируете, что и когда будете делать", "Б) Б) уходите без определенного плана" }),
                new Question("Находясь в компании, Вы обычно:", new[] { "А) присоединяетесь к общему разговору", "Б) беседуете время от времени с кем-то одним" }),
                new Question("Вам легче поладить с людьми:", new[] { "А) имеющими богатое воображение", "Б) реалистичными" }),
                new Question("Более высокой похвалой Вы считаете слова:", new[] { "А) душевный человек", "Б) последовательно рассуждающий человек" }),
                new Question("Вы предпочитаете:", new[] { "А) заранее договариваться о встречах, вечеринках и т.п.", "Б) иметь возможность в последний момент решать, как развлечься" }),
                new Question("В большой компании чаще:", new[] { "А) Вы представляете людей друг другу", "Б) Вас знакомят с другими" }),
                new Question("Вас скорее можно назвать:", new[] { "А) человеком практичным", "Б) выдумщиком" }),
                new Question("Обычно Вы:", new[] { "А) цените чувства больше, чем логику", "Б) цените логику больше, чем чувства" }),
                new Question("Вы чаще добиваетесь успеха:", new[] { "А) действуя в непредсказуемой ситуации, когда нужно быстро принимать решения", "Б) следуя тщательно разработанному плану" }),
                new Question("Вы предпочитаете:", new[] { "А) иметь несколько близких, верных друзей", "Б) иметь дружеские связи с самыми разными людьми" }),
                new Question("Вам больше нравятся люди, которые:", new[] { "А) следуют общепринятым нормам и не привлекают к себе внимания", "Б) настолько оригинальны, что им все равно, обращают на них внимание или нет" }),
                new Question("На Ваш взгляд самый большой недостаток – быть:", new[] { "А) бесчувственным", "Б) неблагоразумным" }),
                new Question("Следование какому-либо расписанию:", new[] { "А) привлекает Вас", "Б) сковывает Вас" }),
                new Question("Среди своих друзей Вы:", new[] { "А) позже других узнаете о событиях в их жизни", "Б) обычно знаете массу новостей о них" }),
                new Question("Вы бы предпочли иметь среди своих друзей человека, который:", new[] { "А) всегда полон новых идей", "Б) трезво и реалистично смотрит на мир" }),
                new Question("Вы предпочли бы работать под началом человека, который:", new[] { "А) всегда добр", "Б) всегда справедлив" }),
                new Question("Мысль о том, чтобы составить список дел на выходные:", new[] { "А) Вас привлекает", "Б) оставляет Вас равнодушным", "В) угнетает Вас" }),
                new Question("Вы обычно:", new[] { "А) можете легко разговаривать практически с любым человеком в течение любого времени", "Б) можете найти тему для разговора только с немногими людьми и только в определенных ситуациях" }),
                new Question("Когда Вы читаете для своего удовольствия, Вам нравится:", new[] { "А) необычная, оригинальная манера изложения", "Б) когда писатели четко выражают свои мысли" }),
                new Question("Вы считаете, что более серьезный недостаток:", new[] { "А) быть слишком сердечным", "Б) быть недостаточно сердечным" }),
                new Question("В своей повседневной работе:", new[] { "А) Вам больше нравятся критические ситуации, когда Вам приходится работать в условиях дефицита времени", "Б) ненавидите работать в жестких временных рамках", "В) обычно планируете свою работу так, чтобы Вам хватило времени" }),
                new Question("Люди могут определить область Ваших интересов:", new[] { "А) при первом знакомстве с Вами", "Б) лишь тогда, когда узнают Вас поближе" }),
                new Question("Выполняя ту же работу, что и многие другие люди, Вы предпочитаете:", new[] { "А) делать это традиционным способом", "Б) изобретать свой собственный способ" }),
                new Question("Вас больше волнуют:", new[] { "А) чувства людей", "Б) их права" }),
                new Question("Когда Вам нужно выполнить определенную работу, Вы обычно:", new[] { "А) тщательно организовываете все перед началом работы", "Б) предпочитаете выяснять все необходимое в процессе работы" }),
                new Question("Обычно Вы:", new[] { "А) свободно выражаете свои чувства", "Б) держите свои чувства при себе" }),
                new Question("Вы предпочитаете:", new[] { "А) быть оригинальным", "Б) следовать общепринятым нормам" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) кроткий", "Б) настойчивый" }),
                new Question("Когда Вам необходимо что-то сделать в определенное время, Вы считаете, что:", new[] { "А) лучше планировать все заранее", "Б) несколько неприятно быть связанным этими планами" }),
                new Question("Можно сказать, что Вы:", new[] { "А) более восторженны по сравнению с другими людьми", "Б) менее восторженны, чем большинство людей" }),
                new Question("Более высокой похвалой человеку будет признание:", new[] { "А) его способности к предвидению", "Б) его здравого смысла" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) мысли", "Б) чувства" }),
                new Question("Обычно:", new[] { "А) Вы предпочитаете все делать в последнюю минуту", "Б) для Вас откладывать все до последней минуты – это слишком большая нервотрепка" }),
                new Question("На вечеринках Вам:", new[] { "А) иногда становится скучно", "Б) всегда весело" }),
                new Question("Вы считаете, что более важно:", new[] { "А) видеть различные возможности в какой-либо ситуации", "Б) воспринимать факты такими, какие они есть" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) убедительный", "Б) трогательный" }),
                new Question("Считаете ли Вы, что наличие стабильного повседневного распорядка:", new[] { "А) очень удобно для выполнения многих дел", "Б) тягостно, даже когда это необходимо" }),
                new Question("Когда что-то входит в моду, Вы обычно:", new[] { "А) одним из первых испробуете это", "Б) нмало этим интересуетесь" }),
                new Question("Вы скорее:", new[] { "А) придерживаетесь общепринятых методов в работе", "Б) ищете, что еще неверно, и беретесь за неразрешенные проблемы" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) анализировать", "Б) сопереживать" }),
                new Question("Когда Вы думаете о том, что надо сделать какое-то не очень важное дело или купить какую-то мелкую вещь, Вы:", new[] { "А) часто забываете об этом и вспоминаете слишком поздно", "Б) записываете это на бумаге, чтобы не забыть", "В) всегда выполняете это без дополнительных напоминаний" }),
                new Question("Узнать, что Вы за человек:", new[] { "А) довольно легко", "Б) довольно трудно" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) факты", "Б) идеи" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) справедливость", "Б) сочувствие" }),
                new Question("Вам труднее приспособиться:", new[] { "А) к однообразию", "Б) к постоянным изменениям" }),
                new Question("Оказавшись в затруднительной ситуации, Вы обычно:", new[] { "А) переводите разговор на другое", "Б) превращаете его в шутку", "В) спустя несколько дней думаете, что Вам следовало сказать" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) утверждение", "Б) идея" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) сочувствие", "Б) расчетливость" }),
                new Question("Когда Вы начинаете какое-то большое дело, которое займет у Вас неделю, Вы:", new[] { "А) составляете сначала список того, что нужно сделать и в каком порядке", "Б) сразу беретесь за работу" }),
                new Question("Вы считаете, что Вашим близким известны Ваши мысли:", new[] { "А) достаточно хорошо", "Б) лишь тогда, когда Вы намеренно сообщаете о них" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) теория", "Б) факт" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) выгода", "Б) благодеяние" }),
                new Question("Выполняя какую-либо работу, Вы обычно:", new[] { "А) планируете работу таким образом, чтобы закончить с запасом времени", "Б) в последний момент работаете с наивысшей производительностью" }),
                new Question("Будучи на вечеринке, Вы предпочитаете:", new[] { "А) активно участвовать в развитии событий", "Б) предоставляете другим развлекаться, как им хочется" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) буквальный", "Б) фигуральный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) решительный", "Б) преданный, любящий" }),
                new Question("Если в выходной утром Вас спросят, что Вы собираетесь сделать в течение дня, Вы:", new[] { "А) сможете довольно точно ответить", "Б) перечислите вдвое больше дел, чем сможете сделать", "В) предпочтете не загадывать заранее" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) энергичный", "Б) спокойный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) образный", "Б) прозаичный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) неуступчивый", "Б) добросердечный" }),
                new Question("Однообразие повседневных дел кажется Вам:", new[] { "А) спокойным", "Б) утомительным" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) сдержанный", "Б) разговорчивый" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) производить", "Б) создавать" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) миротворец", "Б) судья" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) запланированный", "Б) внеплановый" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) спокойный", "Б) оживленный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) благоразумный", "Б) очаровательный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) мягкий", "Б) твердый" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) методичный", "Б) спонтанный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) говорить", "Б) писать" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) производство", "Б) планирование" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) прощать", "Б) дозволять" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) систематический", "Б) случайный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) общительный", "Б) замкнутый" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) конкретный", "Б) абстрактный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) кто", "Б) что" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) импульс", "Б) решение" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) вечеринка", "Б) театр" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) сооружать", "Б) изобретать" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) некритичный", "Б) критичный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) пунктуальный", "Б) свободный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) основание", "Б) вершина" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) осторожный", "Б) доверчивый" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) переменчивый", "Б) неизменный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) теория", "Б) практика" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) соглашаться", "Б) дискутировать" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) дисциплинированный", "Б) беспечный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) знак", "Б) символ" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) стремительный", "Б) тщательный" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) принимать", "Б) изменять" }),
                new Question("Какое слово в паре Вам нравится\n(ориентируйтесь на значение слова, а не на то, как оно выглядит или звучит).", new[] { "А) известный", "Б) неизвестный" })
    };

        private readonly IDbConnection _dbConnection;
        private static readonly Dictionary<ulong, UserTestState> UserTestStates = new();

        public TestModule(IDbConnection dbConnection)
        {
            _dbConnection = dbConnection;
        }



        [Command("starttest")]
        public async Task StartTestAsync()
        {

            var user = Context.User as SocketGuildUser;
            if (!user.Roles.Any(r => r.Name == "authorize"))
            {
                await ReplyAsync("Вам необходимо пройти авторизацию, используя команду !authorization.");
                return;
            }
            else
            {

                if (UserTestStates.ContainsKey(Context.User.Id))
                {
                    await ReplyAsync("Ты уже начал тест. Пожалуйста, заверши тест.");
                    return;
                }

                var testState = new UserTestState();
                UserTestStates[Context.User.Id] = testState;

                await ReplyAsync("Выберите пол (М/Ж), чтобы дать ответ на вопрос используй команду !answer м или ж!");
            }
        }

        [Command("answer")]
        public async Task AnswerAsync(string answer)
        {
            var user = Context.User as SocketGuildUser;

            if (!user.Roles.Any(r => r.Name == "authorize"))
            {
                await ReplyAsync("Вам необходимо пройти авторизацию, используя команду !authorization.");
                return;
            }
            else
            {

                if (!UserTestStates.TryGetValue(Context.User.Id, out var testState))
                {
                    await ReplyAsync("Пожалуйста, начните тест, используя команду `!starttest`.");
                    return;
                }

                if (string.IsNullOrEmpty(testState.Gender))
                {
                    if (answer.ToUpper() == "М" || answer.ToUpper() == "Ж")
                    {
                        testState.Gender = answer.ToUpper();
                        await AskQuestionAsync(testState);
                    }
                    else
                    {
                        await ReplyAsync("Пожалуйста, выберите пол (М/Ж):");
                    }
                    return;
                }

                if (testState.CurrentQuestionIndex == 19 || testState.CurrentQuestionIndex == 23 || testState.CurrentQuestionIndex == 43 || testState.CurrentQuestionIndex == 48 || testState.CurrentQuestionIndex == 59)
                {
                    if (!"АБВ".Contains(answer.ToUpper()))
                    {
                        await ReplyAsync("Пожалуйста, выберите ответ (А, Б, В), чтобы дать ответ на вопрос используй команду !answer а, б или в!");
                        return;
                    }
                }
                else
                {
                    if (!"АБ".Contains(answer.ToUpper()))
                    {
                        await ReplyAsync("Пожалуйста, выберите ответ (А, Б), чтобы дать ответ на вопрос используй команду !answer а или б!");
                        return;
                    }
                }

                testState.Answers.Add(answer.ToUpper()[0]);
                testState.CurrentQuestionIndex++;

                if (testState.CurrentQuestionIndex < Questions.Count)
                {
                    await AskQuestionAsync(testState);
                }
                else
                {
                    var type = CalculateMBTIType(testState);
                    UserTestStates.Remove(Context.User.Id);
                    if (user != null)
                    {
                        await UserEndTest(user, type);
                    }
                }
            }
        }

        private async Task AskQuestionAsync(UserTestState testState)
        {
            var question = Questions[testState.CurrentQuestionIndex];
            var questionText = $"{testState.CurrentQuestionIndex + 1}. {question.Text}\n{string.Join("\n", question.Options)}";
            await ReplyAsync(questionText);
        }

        private string CalculateMBTIType(UserTestState testState)
        {
            var scores = new Dictionary<string, int>
            {
                { "E", 0 }, { "I", 0 }, { "S", 0 }, { "N", 0 },
                { "T", 0 }, { "F", 0 }, { "J", 0 }, { "P", 0 }
            };

            for (int i = 0; i < testState.Answers.Count; i++)
            {
                char response = testState.Answers[i];
                switch (i)
                {
                    case 0:
                        if (response == 'А') scores["E"] += 2;
                        else if (response == 'Б') scores["I"] += 2;
                        break;

                    case 1:
                        if (response == 'А') scores["S"] += 2;
                        else if (response == 'Б') scores["N"] += 2;
                        break;

                    case 2:
                        if (testState.Gender == "м" || testState.Gender == "М")
                        {
                            if (response == 'А') scores["F"] += 2;
                            else if (response == 'Б') scores["T"] += 1;
                        }

                        else if (testState.Gender == "ж" || testState.Gender == "Ж")
                        {
                            if (response == 'А') scores["F"] += 1;
                            else if (response == 'Б') scores["T"] += 2;

                        }
                        break;

                    case 3:
                        if (response == 'А') scores["J"] += 2;
                        else if (response == 'Б') scores["P"] += 2;
                        break;

                    case 4:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 2;
                        break;

                    case 5:
                        if (response == 'А') scores["N"] += 2;
                        else if (response == 'Б') scores["S"] += 1;
                        break;

                    case 6:
                        if (testState.Gender == "м" || testState.Gender == "М")
                        {
                            if (response == 'А') scores["F"] += 1;
                            else if (response == 'Б') scores["T"] += 2;
                        }

                        else if (testState.Gender == "ж" || testState.Gender == "Ж")
                        {
                            if (response == 'А') scores["F"] += 1;
                            else if (response == 'Б') scores["T"] += 2;

                        }
                        break;

                    case 7:
                        if (response == 'А') scores["J"] += 2;
                        else if (response == 'Б') scores["P"] += 2;
                        break;

                    case 8:
                        if (response == 'А') scores["E"] += 2;
                        else if (response == 'Б') scores["I"] += 2;
                        break;

                    case 9:
                        if (response == 'А') scores["S"] += 2;
                        else if (response == 'Б') scores["N"] += 2;
                        break;

                    case 10:
                        if (response == 'А') scores["F"] += 2;
                        else if (response == 'Б') scores["T"] += 2;
                        break;

                    case 11:
                        if (response == 'А') scores["P"] += 1;
                        else if (response == 'Б') scores["J"] += 1;
                        break;

                    case 12:
                        if (response == 'А') scores["I"] += 1;
                        else if (response == 'Б') scores["E"] += 2;
                        break;

                    case 13:
                        if (response == 'А') scores["S"] += 1;
                        else if (response == 'Б') scores["N"] += 2;
                        break;

                    case 14:
                        if (response == 'А') scores["F"] += 2;
                        else if (response == 'Б') scores["T"] += 0;
                        break;

                    case 15:
                        if (response == 'А') scores["J"] += 2;
                        else if (response == 'Б') scores["P"] += 2;
                        break;

                    case 16:
                        if (response == 'А') scores["I"] += 1;
                        else if (response == 'Б') scores["E"] += 2;
                        break;

                    case 17:
                        if (response == 'А') scores["N"] += 1;
                        else if (response == 'Б') scores["S"] += 2;
                        break;

                    case 18:
                        if (testState.Gender == "м" || testState.Gender == "М")
                        {
                            if (response == 'А') scores["F"] += 2;
                            else if (response == 'Б') scores["T"] += 0;
                        }

                        else if (testState.Gender == "ж" || testState.Gender == "Ж")
                        {
                            if (response == 'А') scores["F"] += 1;
                            else if (response == 'Б') scores["T"] += 0;

                        }
                        break;

                    case 19:
                        if (response == 'А') scores["J"] += 1;
                        else if (response == 'Б') scores["P"] += 1;
                        else if (response == 'В') scores["P"] += 1;
                        break;

                    case 20:
                        if (response == 'А') scores["E"] += 2;
                        else if (response == 'Б') scores["I"] += 2;
                        break;

                    case 21:
                        if (response == 'А') scores["N"] += 0;
                        else if (response == 'Б') scores["S"] += 1;
                        break;

                    case 22:
                        if (response == 'А') scores["T"] += 1;
                        else if (response == 'Б') scores["F"] += 0;
                        break;

                    case 23:
                        if (response == 'А') scores["P"] += 1;
                        else if (response == 'Б') scores["P"] += 0;
                        else if (response == 'В') scores["J"] += 1;
                        break;

                    case 24:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 1;
                        break;

                    case 25:
                        if (response == 'А') scores["S"] += 1;
                        else if (response == 'Б') scores["N"] += 1;
                        break;

                    case 26:
                        if (testState.Gender == "м" || testState.Gender == "М")
                        {
                            if (response == 'А') scores["F"] += 0;
                            else if (response == 'Б') scores["T"] += 2;
                        }

                        else if (testState.Gender == "ж" || testState.Gender == "Ж")
                        {
                            if (response == 'А') scores["F"] += 0;
                            else if (response == 'Б') scores["T"] += 1;

                        }
                        break;

                    case 27:
                        if (response == 'А') scores["J"] += 1;
                        else if (response == 'Б') scores["P"] += 2;
                        break;

                    case 28:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 0;
                        break;

                    case 29:
                        if (response == 'А') scores["S"] += 1;
                        else if (response == 'Б') scores["N"] += 0;
                        break;

                    case 30:
                        if (response == 'А') scores["F"] += 1;
                        else if (response == 'Б') scores["T"] += 2;
                        break;

                    case 31:
                        if (response == 'А') scores["J"] += 1;
                        else if (response == 'Б') scores["P"] += 1;
                        break;

                    case 32:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 1;
                        break;

                    case 33:
                        if (response == 'А') scores["N"] += 2;
                        else if (response == 'Б') scores["S"] += 1;
                        break;

                    case 34:
                        if (testState.Gender == "м" || testState.Gender == "М")
                        {
                            if (response == 'А') scores["T"] += 2;
                            else if (response == 'Б') scores["F"] += 1;
                        }

                        else if (testState.Gender == "ж" || testState.Gender == "Ж")
                        {
                            if (response == 'А') scores["T"] += 2;
                            else if (response == 'Б') scores["F"] += 2;

                        }
                        break;

                    case 35:
                        if (response == 'А') scores["P"] += 1;
                        else if (response == 'Б') scores["J"] += 1;
                        break;

                    case 36:
                        if (response == 'А') scores["I"] += 1;
                        else if (response == 'Б') scores["E"] += 2;
                        break;

                    case 37:
                        if (response == 'А') scores["N"] += 0;
                        else if (response == 'Б') scores["S"] += 1;
                        break;

                    case 38:
                        if (testState.Gender == "м" || testState.Gender == "М")
                        {
                            if (response == 'А') scores["T"] += 2;
                            else if (response == 'Б') scores["F"] += 1;
                        }

                        else if (testState.Gender == "ж" || testState.Gender == "Ж")
                        {
                            if (response == 'А') scores["T"] += 2;
                            else if (response == 'Б') scores["F"] += 2;

                        }
                        break;

                    case 39:
                        if (response == 'А') scores["P"] += 1;
                        else if (response == 'Б') scores["P"] += 2;
                        break;

                    case 40:
                        if (response == 'А') scores["E"] += 0;
                        else if (response == 'Б') scores["I"] += 2;
                        break;

                    case 41:
                        if (response == 'А') scores["S"] += 2;
                        else if (response == 'Б') scores["N"] += 0;
                        break;

                    case 42:
                        if (testState.Gender == "м" || testState.Gender == "М")
                        {
                            if (response == 'А') scores["T"] += 1;
                            else if (response == 'Б') scores["F"] += 2;
                        }

                        else if (testState.Gender == "ж" || testState.Gender == "Ж")
                        {
                            if (response == 'А') scores["T"] += 2;
                            else if (response == 'Б') scores["F"] += 2;

                        }
                        break;

                    case 43:
                        if (response == 'А') scores["P"] += 1;
                        else if (response == 'Б') scores["J"] += 1;
                        else if (response == 'В') scores["P"] += 1;
                        break;

                    case 44:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 2;
                        break;

                    case 45:
                        if (response == 'А') scores["S"] += 2;
                        else if (response == 'Б') scores["N"] += 1;
                        break;

                    case 46:
                        if (response == 'А') scores["T"] += 1;
                        else if (response == 'Б') scores["F"] += 2;
                        break;

                    case 47:
                        if (response == 'А') scores["P"] += 1;
                        else if (response == 'Б') scores["J"] += 1;
                        break;

                    case 48:
                        if (response == 'А') scores["E"] += 0;
                        else if (response == 'Б') scores["E"] += 1;
                        else if (response == 'В') scores["I"] += 2;
                        break;

                    case 49:
                        if (response == 'А') scores["S"] += 2;
                        else if (response == 'Б') scores["N"] += 1;
                        break;

                    case 50:
                        if (response == 'А') scores["F"] += 1;
                        else if (response == 'Б') scores["T"] += 2;
                        break;

                    case 51:
                        if (response == 'А') scores["J"] += 2;
                        else if (response == 'Б') scores["P"] += 1;
                        break;

                    case 52:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 1;
                        break;

                    case 53:
                        if (response == 'А') scores["N"] += 2;
                        else if (response == 'Б') scores["S"] += 1;
                        break;

                    case 54:
                        if (response == 'А') scores["T"] += 1;
                        else if (response == 'Б') scores["F"] += 1;
                        break;

                    case 55:
                        if (response == 'А') scores["P"] += 1;
                        else if (response == 'Б') scores["J"] += 0;
                        break;

                    case 56:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 2;
                        break;

                    case 57:
                        if (response == 'А') scores["S"] += 1;
                        else if (response == 'Б') scores["N"] += 1;
                        break;

                    case 58:
                        if (testState.Gender == "м" || testState.Gender == "М")
                        {
                            if (response == 'А') scores["T"] += 1;
                            else if (response == 'Б') scores["F"] += 2;
                        }

                        else if (testState.Gender == "ж" || testState.Gender == "Ж")
                        {
                            if (response == 'А') scores["T"] += 1;
                            else if (response == 'Б') scores["F"] += 1;

                        }
                        break;

                    case 59:
                        if (response == 'А') scores["J"] += 0;
                        else if (response == 'Б') scores["P"] += 1;
                        else if (response == 'В') scores["P"] += 1;
                        break;

                    case 60:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 2;
                        break;

                    case 61:
                        if (response == 'А') scores["N"] += 0;
                        else if (response == 'Б') scores["S"] += 2;
                        break;

                    case 62:
                        if (response == 'А') scores["T"] += 2;
                        else if (response == 'Б') scores["F"] += 0;
                        break;

                    case 63:
                        if (response == 'А') scores["J"] += 1;
                        else if (response == 'Б') scores["P"] += 0;
                        break;

                    case 64:
                        if (response == 'А') scores["I"] += 1;
                        else if (response == 'Б') scores["E"] += 2;
                        break;

                    case 65:
                        if (response == 'А') scores["S"] += 2;
                        else if (response == 'Б') scores["N"] += 0;
                        break;

                    case 66:
                        if (response == 'А') scores["F"] += 0;
                        else if (response == 'Б') scores["T"] += 2;
                        break;

                    case 67:
                        if (response == 'А') scores["J"] += 2;
                        else if (response == 'Б') scores["P"] += 2;
                        break;

                    case 68:
                        if (response == 'А') scores["I"] += 1;
                        else if (response == 'Б') scores["E"] += 1;
                        break;

                    case 69:
                        if (response == 'А') scores["S"] += 2;
                        else if (response == 'Б') scores["N"] += 0;
                        break;

                    case 70:
                        if (response == 'А') scores["F"] += 0;
                        else if (response == 'Б') scores["T"] += 2;
                        break;

                    case 71:
                        if (response == 'А') scores["J"] += 2;
                        else if (response == 'Б') scores["P"] += 2;
                        break;

                    case 72:
                        if (response == 'А') scores["E"] += 0;
                        else if (response == 'Б') scores["I"] += 1;
                        break;

                    case 73:
                        if (response == 'А') scores["S"] += 1;
                        else if (response == 'Б') scores["N"] += 1;
                        break;

                    case 74:
                        if (response == 'А') scores["F"] += 0;
                        else if (response == 'Б') scores["T"] += 2;
                        break;

                    case 75:
                        if (response == 'А') scores["J"] += 2;
                        else if (response == 'Б') scores["P"] += 2;
                        break;

                    case 76:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 1;
                        break;

                    case 77:
                        if (response == 'А') scores["S"] += 1;
                        else if (response == 'Б') scores["N"] += 2;
                        break;

                    case 78:
                        if (response == 'А') scores["F"] += 0;
                        else if (response == 'Б') scores["T"] += 1;
                        break;

                    case 79:
                        if (response == 'А') scores["P"] += 2;
                        else if (response == 'Б') scores["J"] += 1;
                        break;

                    case 80:
                        if (response == 'А') scores["E"] += 1;
                        else if (response == 'Б') scores["I"] += 0;
                        break;

                    case 81:
                        if (response == 'А') scores["S"] += 2;
                        else if (response == 'Б') scores["N"] += 1;
                        break;

                    case 82:
                        if (response == 'А') scores["F"] += 1;
                        else if (response == 'Б') scores["T"] += 1;
                        break;

                    case 83:
                        if (response == 'А') scores["J"] += 1;
                        else if (response == 'Б') scores["P"] += 1;
                        break;

                    case 84:
                        if (response == 'А') scores["N"] += 2;
                        else if (response == 'Б') scores["S"] += 0;
                        break;

                    case 85:
                        if (response == 'А') scores["T"] += 2;
                        else if (response == 'Б') scores["F"] += 0;
                        break;

                    case 86:
                        if (response == 'А') scores["P"] += 0;
                        else if (response == 'Б') scores["J"] += 1;
                        break;

                    case 87:
                        if (response == 'А') scores["N"] += 2;
                        else if (response == 'Б') scores["S"] += 0;
                        break;

                    case 88:
                        if (response == 'А') scores["F"] += 1;
                        else if (response == 'Б') scores["T"] += 0;
                        break;

                    case 89:
                        if (response == 'А') scores["J"] += 2;
                        else if (response == 'Б') scores["P"] += 1;
                        break;

                    case 90:
                        if (response == 'А') scores["S"] += 1;
                        else if (response == 'Б') scores["N"] += 0;
                        break;

                    case 91:
                        if (response == 'А') scores["P"] += 1;
                        else if (response == 'Б') scores["J"] += 0;
                        break;

                    case 92:
                        if (response == 'А') scores["S"] += 1;
                        else if (response == 'Б') scores["N"] += 0;
                        break;

                    case 93:
                        if (response == 'А') scores["S"] += 1;
                        else if (response == 'Б') scores["N"] += 1;
                        break;
                }
            }

            string type = "";


            double relation_E = (double)scores["E"] / 26;
            double relation_I = (double)scores["I"] / 28;
            double relation_N = (double)scores["N"] / 26;
            double relation_S = (double)scores["S"] / 34;
            double relation_T = (double)scores["T"] / 32;
            double relation_F = (double)scores["F"] / 22;
            double relation_J = (double)scores["J"] / 28;
            double relation_P = (double)scores["P"] / 30;

            double ratio_E_I = relation_E + relation_I;
            double ratio_N_S = relation_N + relation_S;
            double ratio_T_F = relation_T + relation_F;
            double ratio_J_P = relation_J + relation_P;

            double severity_E = relation_E / ratio_E_I * 100;
            double severity_I = relation_I / ratio_E_I * 100;
            double severity_N = relation_N / ratio_N_S * 100;
            double severity_S = relation_S / ratio_N_S * 100;
            double severity_T = relation_T / ratio_T_F * 100;
            double severity_F = relation_F / ratio_T_F * 100;
            double severity_J = relation_J / ratio_J_P * 100;
            double severity_P = relation_P / ratio_J_P * 100;



            if (severity_I > severity_E)
            {
                type += "I";
            }
            else
            {
                type += "E";
            }

            if (severity_S > severity_N)
            {
                type += "S";
            }
            else
            {
                type += "N";
            }

            if (severity_F > severity_T)
            {
                type += "F";
            }
            else
            {
                type += "T";
            }

            if (severity_P > severity_J)
            {
                type += "P";
            }
            else
            {
                type += "J";
            }

            return type;
        }

        private string GetTypeDescription(string type)
        {
            var descriptions = new Dictionary<string, string>
        {


            { "ENFJ" , "Гамлет: Мотиватор | Душа команды | Исследователь (ресурсов) | Генератор идей" },
            { "ESTP" , "Жуков: Мотиватор | Координатор | Исследователь (ресурсов)" },
            { "ESFP" , "Наполеон: Мотиватор | Душа команды | Исследователь (ресурсов)"},
            { "ENTP" , "Дон Кихот: Мотиватор | Генератор идей"},
            { "ENFP" , "Гексли: Мотиватор | Душа команды"},
            { "ESFJ" , "Гюго: Мотиватор | Координатор | Душа команды | Исследователь (ресурсов)"},
            { "ENTJ" , "Джек Лондон: Мотиватор | Координатор | Исследователь (ресурсов) | Генератор идей | Аналитик"},
            { "ISTJ" , "Максим Горький: Исполнитель (реализатор) | Педант (контролёр) | Координатор | Специалист"},
            { "ISFJ" , "Драйзер: Исполнитель (реализатор) | Педант (контролёр)"},
            { "INTJ" , "Робеспьер: Исполнитель (реализатор) | Педант (контролёр) | Генератор идей | Аналитик | Специалист"},
            { "INFJ" , "Достоевский: Исполнитель (реализатор) | Педант (контролёр) | Душа команды | Генератор идей"},
            { "INTP" , "Бальзак: Исполнитель (реализатор) | Педант (контролёр) | Генератор идей | Аналитик | Специалист"},
            { "ISTP" , "Габен: Исполнитель (реализатор) | Специалист"},
            { "ISFP" , "Дюма: Исполнитель (реализатор) | Душа команды"},
            { "INFP" , "Есенин: Исполнитель (реализатор) | Душа команды"},
            { "ESTJ" , "Штирлиц: Исполнитель (реализатор) | Педант (контролёр) | Координатор | Исследователь (ресурсов) | Аналитик | Специалист"},
        };


            return descriptions.ContainsKey(type) ? descriptions[type] : "Описание недоступно.";

        }

        private async Task UserEndTest(SocketGuildUser user, string type)
        {
            var notVerificateRole = user.Guild.Roles.FirstOrDefault(r => r.Name == "notverificate");
            if (notVerificateRole != null)
            {
                try
                {
                    await user.RemoveRoleAsync(notVerificateRole);
                    Console.WriteLine($"Role 'notverificate' removed from user: {user.Username}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error removing role: {ex.Message}");
                }
            }

            var mbtiRole = user.Guild.Roles.FirstOrDefault(r => r.Name == type);
            var VeriRole = user.Guild.Roles.FirstOrDefault(r => r.Name == "verificate");
            if (mbtiRole != null)
            {
                try
                {
                    await user.AddRoleAsync(mbtiRole);
                    await user.AddRoleAsync(VeriRole);
                    Console.WriteLine($"Role '{type}' added to user: {user.Username}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error adding role: {ex.Message}");
                }
            }

            try
            {
                long userId = Convert.ToInt64(user.Id);
                await _dbConnection.ExecuteAsync(
                    "UPDATE \"User\" SET user_mbti = @Type WHERE user_discord_tag = @Tag",
                    new { Type = type, Tag = Context.User.Username}
                );
                Console.WriteLine($"MBTI type '{type}' updated for user: {user.Username}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating MBTI type in database: {ex.Message}");
            }

            try
            {
                var dmChannel = await user.CreateDMChannelAsync();
                await dmChannel.SendMessageAsync($"Поздравляем! Ты завершил тест MBTI, теперь тебе доступны основные каналы");
                Console.WriteLine($"Message sent to user: {user.Username}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending DM: {ex.Message}");
            }
        }



        private class Question
        {
            public string Text { get; }
            public string[] Options { get; }

            public Question(string text, string[] options)
            {
                Text = text;
                Options = options;
            }
        }

        private class UserTestState
        {
            public string Gender { get; set; }
            public int CurrentQuestionIndex { get; set; } = 0;
            public List<char> Answers { get; set; } = new List<char>();
        }
    }
}
