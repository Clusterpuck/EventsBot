using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.SlashCommands;

namespace EventsBotMain
{
    public class BasicSL : ApplicationCommandModule
    {
        [SlashCommand("optin", "Get messages from EventsBot")]
        public async Task OptInCommand( InteractionContext ctx )
        {
            await ctx.Channel.SendMessageAsync("Hello Slasher");
        }
    }
}
