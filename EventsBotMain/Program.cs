namespace EventsBot
{
    using Discord;
    using Discord.Rest;
    using Discord.WebSocket;
    using Ical.Net;
    using Ical.Net.CalendarComponents;
    using Ical.Net.DataTypes;
    using Ical.Net.Serialization;
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using Azure.Identity;
    using Azure.Security.KeyVault.Secrets;
    using Azure.Core;
    using EventsBotMain.Properties;

    class Program
    {
        private DiscordSocketClient _client;

        static async Task Main(string[] args)
        {
            Program program = new Program();
            await program.RunBotAsync();
        }

        public async Task RunBotAsync()
        {
            _client = new DiscordSocketClient();
            _client.Log += Log;
            //Populates log of created events for populating fields
            _client.GuildScheduledEventCreated += HandleNewEvent;
            _client.GuildScheduledEventUserAdd += MessageUser;
            string discordToken = Resources.DISCORD_TOKEn;

            await _client.LoginAsync(TokenType.Bot, discordToken);
            await _client.StartAsync();

            await Task.Delay(-1);

        }

     

        private Task Log(LogMessage arg)
        {
            Console.WriteLine(arg);
            return Task.CompletedTask;
        }

        //TODO Implement this to use Vault Key instead of Resources
        private string getKey()
        {
            try
            {
                SecretClientOptions options = new SecretClientOptions()
                {
                    Retry =
                {
                    Delay= TimeSpan.FromSeconds(2),
                    MaxDelay = TimeSpan.FromSeconds(16),
                    MaxRetries = 5,
                    Mode = Azure.Core.RetryMode.Exponential
                 }
                };
                var client = new SecretClient(new Uri("https://bottokens.vault.azure.net/"), new DefaultAzureCredential(), options);

                KeyVaultSecret secret = client.GetSecret("EventsManagerToken");
                return secret.Value;
            }
            catch( Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
            

            
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
                if (user != null)
                {
                    Console.WriteLine("Got user " + user.GlobalName);
                    // Get or create the direct message channel with the user
                    var dmChannel = user.CreateDMChannelAsync().Result; // Note: Use Result to make it synchronous

                    Console.WriteLine("Got dm channel " + dmChannel.Name);

                    //Create the ics file from the event
                    Ical.Net.Calendar eventCalendar = makeCalendarEvent(ev);
                    var serializer = new CalendarSerializer();
                    var serializedCalendar = serializer.SerializeToString(eventCalendar);

                    // Save the serialized iCal to a file
                    var filePath = $"{ev.Name}_{ev.Guild}.ics";
                    System.IO.File.WriteAllText(filePath, serializedCalendar);
                    await dmChannel.SendFileAsync(filePath, $"I'm sure {ev.Guild} are stoked to hear you're coming to {ev.Name}, {user.Mention}! Download this file to add the event to your calendar!");

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
                //Due to limitation of timezone difference and offset, subtracts offset and sets timezone to UTC for cimplistic solution
                Start = new CalDateTime(ev.StartTime.Year, ev.StartTime.Month, ev.StartTime.Day, ev.StartTime.Hour - (int)ev.StartTime.Offset.TotalHours, ev.StartTime.Minute, 0, "UTC"),
                //End time can only be set from ev if it is a non voice channel event
                End = endTime,
            };

            calendar.Events.Add(calendarEvent);
            return calendar;
        }
    }

}