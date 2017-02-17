using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Hourai.Model;
using Hourai.Preconditions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hourai.Standard {

public partial class Standard : DatabaseHouraiModule {

  LogSet Logs { get; }
  DiscordShardedClient Client { get; }

  public Standard(DatabaseService db,
                  DiscordShardedClient client,
                  LogSet logs) : base(db) {
    Client = client;
    Logs = logs;
  }

  [Command("echo")]
  [ChannelRateLimit(3, 1)]
  [Remarks("Has the bot repeat what you say")]
  public Task Echo([Remainder] string remainder) => ReplyAsync(remainder);

  [Command("choose")]
  [ChannelRateLimit(3, 1)]
  [Remarks("Chooses between several provided choices. Seperated by spaces. Quote choices with spaces in them.")]
  public Task Choose(params string[] choices) {
    if (choices.Length <= 0)
      return RespondAsync($"There is nothing to choose from!");
    return RespondAsync($"I choose {choices[new Random().Next(choices.Length)]}!");
  }

  [Command("avatar")]
  [ChannelRateLimit(3, 1)]
  [Remarks("Gets the avatar url of the provided users. If no user is provided, your avatar is shown instead.")]
  public Task Avatar(params IGuildUser[] users) {
    IUser[] allUsers = users;
    if (users.Length <= 0)
      allUsers = new[] {Context.Message.Author};
    return RespondAsync(allUsers.Select(u => u.AvatarUrl).Join("\n"));
  }

  [Command("invite")]
  [ChannelRateLimit(1, 1)]
  [Remarks("Provides a invite link to add this bot to your server")]
  public Task Invite() =>
    RespondAsync("Use this link to add me to your server: https://discordapp.com/oauth2/authorize?client_id=208460637368614913&scope=bot&permissions=0xFFFFFFFFFFFF");

  [Command("playing")]
  [ChannelRateLimit(1, 1)]
  [RequireContext(ContextType.Guild)]
  [Remarks("Gets all users currently playing a certain game.")]
  public async Task IsPlaying([Remainder] string game) {
    var guild = Check.NotNull(Context.Guild);
    var users = await guild.GetUsersAsync();
    var regex = new Regex(game, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    var players = from user in users
                  where user.Game.HasValue && regex.IsMatch(user.Game?.Name)
                  group user.Username by user.Game.Value.Name into g
                  select $"{g.Key.Bold()}: {g.Join(", ")}";
    var results = players.Join("\n");
    await RespondAsync(!string.IsNullOrEmpty(results) ? results : "No results.");
  }

  [Command("serverinfo")]
  [ChannelRateLimit(3, 1)]
  [RequireContext(ContextType.Guild)]
  [Remarks("Gets general information about the current server")]
  public async Task ServerInfo() {
    var builder = new StringBuilder();
    var server = Check.NotNull(Context.Guild);
    var guild = DbContext.GetGuild(server);
    var owner = await server.GetOwner();
    var channels = await server.GetChannelsAsync();
    var textChannels = channels.OfType<ITextChannel>().Order().Select(ch => ch.Name.Code());
    var voiceChannels = channels.OfType<IVoiceChannel>().Order().Select(ch => ch.Name.Code());
    var roles = server.Roles.Where(r => r.Id != server.EveryoneRole.Id);
    var socketServer = server as SocketGuild;
    var userCount = socketServer?.MemberCount ?? (await server.GetUsersAsync()).Count;
    builder.AppendLine($"Name: {server.Name.Code()}")
      .AppendLine($"ID: {server.Id.ToString().Code()}")
      .AppendLine($"Owner: {owner.Username.Code()}")
      .AppendLine($"Region: {server.VoiceRegionId.Code()}")
      .AppendLine($"Created: {server.CreatedAt.ToString().Code()}")
      .AppendLine($"User Count: {userCount.ToString().Code()}");
    if(roles.Any())
      builder.AppendLine($"Roles: {roles.Order().Select(r => r.Name.Code()).Join(", ")}");
    builder.AppendLine($"Text Channels: {textChannels.Join(", ")}")
      .AppendLine($"Voice Channels: {voiceChannels.Join(", ")}")
      .AppendLine($"Bot Command Prefix: {guild.Prefix}");
    if(!string.IsNullOrEmpty(server.IconUrl))
      builder.AppendLine(server.IconUrl);
    await Context.Message.Respond(builder.ToString());
  }

  [Command("channelinfo")]
  [ChannelRateLimit(3, 1)]
  [RequireContext(ContextType.Guild)]
  [Remarks("Gets information on a specified channel")]
  public Task ChannelInfo(IGuildChannel channel = null) {
    if(channel == null)
      channel = Check.InGuild(Context.Message);
    return Context.Message.Respond($"ID: {channel.Id.ToString().Code()}");
  }

  [Command("whois")]
  [ChannelRateLimit(3, 1)]
  [Remarks("Gets information on a specified users")]
  public Task WhoIs(IGuildUser user) {
    const int spacing = 80;
    var builder = new StringBuilder()
      .AppendLine($"Username: {user.Username.Code()} {(user.IsBot ? "(BOT)".Code() : string.Empty )}")
      .AppendLine($"Nickname: {user.Nickname.NullIfEmpty()?.Code() ?? "N/A".Code()}")
      .AppendLine($"Current Game: {user.Game?.Name.Code() ?? "N/A".Code()}")
      .AppendLine($"ID: {user.Id.ToString().Code()}")
      .AppendLine($"Joined on: {user.JoinedAt?.ToString().Code() ?? "N/A".Code()}")
      .AppendLine($"Created on: {user.CreatedAt.ToString().Code()}");
    var roles = user.GetRoles().Where(r => r.Id != user.Guild.EveryoneRole.Id);
    var count = Client.Guilds.Where(g => g.GetUser(user.Id) != null).Count();
    if (count > 1)
      builder.AppendLine($"Seen on **{count - 1}** other servers.");
    if(roles.Any())
      builder.AppendLine($"Roles: {roles.Select(r => r.Name.Code()).Join(", ")}");
    if(!string.IsNullOrEmpty(user.AvatarUrl))
      builder.AppendLine(user.AvatarUrl);
    var usernames = DbContext.GetUser(user).Usernames.Where(u => u.Name != user.Username);
    if(usernames.Any()) {
      using(builder.MultilineCode()) {
        foreach(var username in usernames.OrderByDescending(u => u.Date)) {
          builder.Append(username.Name);
          builder.Append(new string(' ', spacing - username.Name.Length));
          builder.AppendLine(username.Date.ToString("yyyy-MM-dd"));
        }
      }
    }
    return RespondAsync(builder.ToString());
  }

  [Command("topic")]
  [ChannelRateLimit(3, 1)]
  [Remarks("Returns the mentioned channels' topics. If none are mentioned, the current channel is used.")]
  public Task Topic(params IGuildChannel[] channels) {
    if(channels.Length <= 0)
      channels = new[] { Context.Channel as IGuildChannel };
    var builder = new StringBuilder();
    foreach(var channel in channels.OfType<ITextChannel>())
      builder.AppendLine($"{channel.Name}: {channel.Topic}");
    return Context.Message.Respond(builder.ToString());
  }

}

}