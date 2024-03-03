namespace EventsBot
{
    using Discord;
    using Discord.Net;
    using Discord.Rest;
    using Discord.WebSocket;
    using EventsBotMain.Properties;
    using Ical.Net.CalendarComponents;
    using Ical.Net.DataTypes;
    using Ical.Net.Serialization;
    using Newtonsoft.Json;
    using System;
    using System.Threading.Tasks;

    class Program
    {
        private static DiscordSocketClient Client { get; set; }
        private HashSet<ulong> optedInUsers;
        static async Task Main(string[] args)
        {
            Program program = new Program();
            await program.RunBotAsync();
        }

        public async Task RunBotAsync()
        {
            AppDomain.CurrentDomain.ProcessExit += ProcessExitHandler;
            Client = new DiscordSocketClient();
            Client.Ready += Client_Ready;

            Timer saveTimer = new Timer(SaveOptedInUsers, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)); // Save every 10 minutes

            Client.Log += Log;
            //Populates log of created events for populating fields
            Client.GuildScheduledEventCreated += HandleNewEvent;
            Client.GuildScheduledEventUserAdd += MessageUser;
            Client.SlashCommandExecuted += HandleSlashCommand;
            
            string discordToken = Resources.DISCORD_TOKEn;

            await Client.LoginAsync(TokenType.Bot, discordToken);
            await Client.StartAsync();

            await Task.Delay(-1);

        }

        private async void ProcessExitHandler(object sender, EventArgs e)
        {
            SaveOptedInUsers(sender); // Save opted-in users to file before exiting
        }

        private async Task Client_Ready()
        {
            optedInUsers = LoadOptedInUsers(); // Load opted-in users from file
            // Let's do our global command
            var globalCommand = new SlashCommandBuilder();
            globalCommand.WithName("optin");
            globalCommand.WithDescription("Choosing to opt in to Events Manager messages");

            var stopCommand = new SlashCommandBuilder();
            stopCommand.WithName("stop");
            stopCommand.WithDescription("Stop EventsManager sending you messages when interested in Events");

            try
            {

                // With global commands we don't need the guild.
                await Client.CreateGlobalApplicationCommandAsync(globalCommand.Build());
                await Client.CreateGlobalApplicationCommandAsync(stopCommand.Build());
            }
            catch (HttpException exception)
            {
                // If our command was invalid, we should catch an ApplicationCommandException. This exception contains the path of the error as well as the error message. You can serialize the Error field in the exception to get a visual of where your error is.
                var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);

                // You can send this error somewhere or just print it to the console, for this example we're just going to print it.
                Console.WriteLine(json);
            }


        }


        private async Task HandleSlashCommand(SocketSlashCommand slashCommand)
        {
            try
            {
                if (slashCommand.CommandName == "optin")
                {
                    Console.WriteLine("Detected optin slash command");
                    // Process opt-in logic
                    await slashCommand.RespondAsync($"Executed {slashCommand.Data.Name}");
                    await OptInUser(slashCommand.User);
                
                }
                else if(slashCommand.CommandName == "stop")
                {
                    await slashCommand.RespondAsync($"Executed {slashCommand.Data.Name}");
                    await OptOutUser(slashCommand.User);
                }

            }
            catch( HttpException exception)
            {
                Console.WriteLine($"Exception in handle slash commannd {exception.Message}");
            }
        }

        private async Task OptInUser(SocketUser user)
        {
            if (!optedInUsers.Contains(user.Id))
            {
                optedInUsers.Add(user.Id);
                await user.SendMessageAsync("You have successfully opted in to the calendar service.");
            }
            else
            {
                await user.SendMessageAsync("You have already opted in to the calendar service.");
            }
        }

        private async Task OptOutUser(SocketUser user)
        {
            if (optedInUsers.Contains(user.Id))
            {
                optedInUsers.Remove(user.Id);
                await user.SendMessageAsync("You have successfully opted out of the calendar service.");
            }
            else
            {
                await user.SendMessageAsync("You are not part of the calendar service. Cannot Opt out");
            }
        }



        private Task Log(LogMessage arg)
        {
            Console.WriteLine(arg);
            return Task.CompletedTask;
        }



        //From detecting when user selected interested in event, sends the user the iCs file for the event
        private async Task MessageUser(Cacheable<SocketUser, RestUser, IUser, ulong> cacheable, SocketGuildEvent ev)
        {
            Console.WriteLine("Detected an interested party");
            try
            {
                var user = await cacheable.GetOrDownloadAsync();

                // Get the user from the cacheable

                // Ensure the user is not null
                if (user != null && optedInUsers.Contains(user.Id) )
                {
                    Console.WriteLine("Got user " + user.GlobalName);
                    // Get or create the direct message channel with the user
                    var dmChannel = user.CreateDMChannelAsync().Result; // Note: Use Result to make it synchronous

                    Console.WriteLine("Got dm channel " + dmChannel.Name);

                    //Create the ics file from the event
                    Ical.Net.Calendar eventCalendar = makeCalendarEvent(ev);
                    var serializer = new CalendarSerializer();
                    var serializedCalendar = serializer.SerializeToString(eventCalendar);

                    string googleLink = GenerateGoogleCalendarLink(ev);

                    // Save the serialized iCal to a file
                    var filePath = $"{ev.Name}_{ev.Guild}.ics";
                    System.IO.File.WriteAllText(filePath, serializedCalendar);
                    await dmChannel.SendFileAsync(filePath, $"I'm sure {ev.Guild} are stoked to hear you're coming to {ev.Name}, {user.Mention}! Download this file to add the event to your calendar! Or add to your google calendar [here]({googleLink})");

                    Console.WriteLine($"iCal file generated and saved to: {filePath}");

                    // You can include additional information or attach files as needed
                }

            }
            catch (Exception ex)
            {
                // Handle exceptions if necessary
                Console.WriteLine($"Error sending message to user: {ex.Message}");
            }
        }


        private string GenerateGoogleCalendarLink(SocketGuildEvent ev)
        {
            // Encode special characters in the event details
            string title = Uri.EscapeDataString(ev.Name);
            string location = "";
            if( ev.Location != null )
            {
                location = Uri.EscapeDataString(ev.Location);
            }
            else
            {
                location = Uri.EscapeDataString(ev.Channel.Name);
            }

            // Set the details parameter to an empty string to avoid promo
            string details = "";

            // Format the start and end times in the required format (YYYYMMDDTHHmmssZ)
            string startTime = ev.StartTime.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
            // Calculate endTime based on ev.EndTime or 1 hour after startTime
            string endTime = ev.EndTime?.ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
                             ?? ev.StartTime.AddHours(1).ToUniversalTime().ToString("yyyyMMddTHHmmssZ");

            // Create the Google Calendar link
            string googleCalendarLink = $"https://www.google.com/calendar/render?action=TEMPLATE&text={title}&location={location}&details={details}&dates={startTime}/{endTime}";

            return googleCalendarLink;
        }



        private async Task HandleNewEvent(SocketGuildEvent ev)
        {
            Console.WriteLine("New Event made " + ev.Name);
            Console.WriteLine("Guild is" + ev.Guild);//server name
            Console.WriteLine("ID is" + ev.Id);//Number unique identifier
            Console.WriteLine("Description is" + ev.Description);//Description given to event
            Console.WriteLine("Channel is" + ev.Channel);//Channel is blank for physical locations
            Console.WriteLine("EndTime Value is" + ev.EndTime.Value);//Time in format example 23/02/2024 7:00:00 PM +08:00
            Console.WriteLine("Location is" + ev.Location);//Physical location is the string or blank for voice channel
            Console.WriteLine("Start Time is" + ev.StartTime);//Time in format example 23/02/2024 7:00:00 PM +08:00
        }

        private Ical.Net.Calendar makeCalendarEvent( SocketGuildEvent ev )
        {
            var calendar = new Ical.Net.Calendar();
            string location;
            CalDateTime endTime = null;
            if (ev.Location == null || ev.Location == "")
            {//Voice channel event, sets end time to default 1 hour after start
                location = ev.Channel.ToString();
                endTime = new CalDateTime(ev.StartTime.Year, ev.StartTime.Month, ev.StartTime.Day, ev.StartTime.Hour - (int)ev.StartTime.Offset.TotalHours + 1, ev.StartTime.Minute, 0, "UTC");
            }
            else
            {//External location event
                location = ev.Location;
                endTime = new CalDateTime(ev.EndTime.Value.Year, ev.EndTime.Value.Month, ev.EndTime.Value.Day, ev.EndTime.Value.Hour - (int)ev.StartTime.Offset.TotalHours, ev.EndTime.Value.Minute, 0, "UTC");
            }

            var calendarEvent = new CalendarEvent
            {
                Summary = ev.Name,
                Description = ev.Description,
                Location = location,
                //Due to limitation of timezone difference and offset, subtracts offset and sets timezone to UTC for simplistic solution
                Start = new CalDateTime(ev.StartTime.Year, ev.StartTime.Month, ev.StartTime.Day, ev.StartTime.Hour - (int)ev.StartTime.Offset.TotalHours, ev.StartTime.Minute, 0, "UTC"),
                //End time can only be set from ev if it is a non voice channel event
                End = endTime,
            };

            calendar.Events.Add(calendarEvent);
            return calendar;
        }

        private void SaveOptedInUsers( object? state )
        {
            Console.WriteLine($"Saving list of {optedInUsers.Count} opted in users");
            string json = JsonConvert.SerializeObject(optedInUsers, Formatting.Indented);
            File.WriteAllText("optedInUsers.json", json);
        }

        private HashSet<ulong> LoadOptedInUsers()
        {
            Console.WriteLine("Starting load opted in");
            if (File.Exists("optedInUsers.json"))
            {
                Console.WriteLine("Found file to load users");
                string json = File.ReadAllText("optedInUsers.json");
                return JsonConvert.DeserializeObject<HashSet<ulong>>(json);
            }
            Console.WriteLine("No file found for users");
            return new HashSet<ulong>();
        }

    }

}