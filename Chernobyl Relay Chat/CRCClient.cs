﻿using Meebey.SmartIrc4net;
using System;
using System.Collections.Generic;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;
using System.Drawing;

#if DEBUG
using System.Threading;
#endif

namespace Chernobyl_Relay_Chat
{
    public class CRCClient
    {
        private const char META_DELIM = '☺'; // Separates metadata
        private const char FAKE_DELIM = '☻'; // Separates fake nick for death messages
        private static readonly Regex metaRx = new Regex("^(.*?)" + META_DELIM + "(.*)$");
        private static readonly Regex deathRx = new Regex("^(.*?)" + FAKE_DELIM + "(.*)$");
        private static readonly Regex userdataRx = new Regex("^(.+?)/(.+?)/(.+)$");

        private static IrcClient client = new IrcClient();
        public static Dictionary<string, Userdata> userData = new Dictionary<string, Userdata>();
        private static DateTime lastDeath = new DateTime();
        private static DateTime lastPay = new DateTime();
        private static DateTime lastMessage = new DateTime();
        private static bool lastStatus = false;
        public static string lastName, lastChannel, prevChannel, lastQuery, lastFaction;
        private static bool retry = false;

#if DEBUG
        private static DebugDisplay debug = new DebugDisplay();
        private static Thread debugThread;
#endif

        public static void Start()
        {
#if DEBUG
            debugThread = new Thread(() => Application.Run(debug));
            debugThread.Start();
#endif
            client.Encoding = Encoding.UTF8;
            client.SendDelay = 200;
            client.ActiveChannelSyncing = true;

            client.OnConnected += new EventHandler(OnConnected);
            client.OnChannelActiveSynced += new IrcEventHandler(OnChannelActiveSynced);
            client.OnRawMessage += new IrcEventHandler(OnRawMessage);
            client.OnChannelMessage += new IrcEventHandler(OnChannelMessage);
            client.OnQueryMessage += new IrcEventHandler(OnQueryMessage);
            client.OnJoin += new JoinEventHandler(OnJoin);
            client.OnPart += new PartEventHandler(OnPart);
            client.OnQuit += new QuitEventHandler(OnQuit);
            client.OnNickChange += new NickChangeEventHandler(OnNickChange);
            client.OnErrorMessage += new IrcEventHandler(OnErrorMessage);
            client.OnKick += new KickEventHandler(OnKick);
            client.OnDisconnected += new EventHandler(OnDisconnected);
            client.OnTopic += new TopicEventHandler(OnTopic);
            client.OnTopicChange += new TopicChangeEventHandler(OnTopicChange);
            client.OnCtcpRequest += new CtcpEventHandler(OnCtcpRequest);
            client.OnCtcpReply += new CtcpEventHandler(OnCtcpReply);

            try
            {
                client.Connect(CRCOptions.Server, 6667);
                client.Listen();
            }
            catch (CouldNotConnectException)
            {
                MessageBox.Show(CRCStrings.Localize("client_connection_error"), CRCStrings.Localize("crc_name"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                CRCDisplay.Stop();
            }
#if DEBUG
            debug.Invoke(new Action(() => debug.Close()));
            debugThread.Join();
#endif
        }

        public static void Stop()
        {
            if (client.IsConnected)
            {
                client.RfcQuit("Safe");
            }
        }

        public static void UpdateSettings()
        {
            if (CRCOptions.Name != lastName)
            {
                client.RfcNick(CRCOptions.Name);
                lastName = CRCOptions.Name;
            }
            if (CRCOptions.GetFaction() != lastFaction)
            {
                if (userData.ContainsKey(CRCOptions.Name))
                    userData[CRCOptions.Name].Faction = CRCOptions.GetFaction();
                client.SendMessage(SendType.CtcpReply, CRCOptions.ChannelProxy(), "AMOGUS " + UserDataUpdate());
                lastFaction = CRCOptions.GetFaction();
            }
        }

        public static void OnChannelSwitch()
        {
            if (CRCOptions.ChannelProxy() != lastChannel)
            {
                userData.Clear();
                client.RfcPart(lastChannel);
                client.RfcJoin(CRCOptions.ChannelProxy());
                lastChannel = CRCOptions.ChannelProxy();
            }
        }

        public static void UpdateStatus()
        {
            if (CRCGame.IsInGame != lastStatus)
            {
                if (userData.ContainsKey(CRCOptions.Name))
                    userData[CRCOptions.Name].IsInGame = CRCGame.IsInGame.ToString();
                client.SendMessage(SendType.CtcpReply, CRCOptions.ChannelProxy(), "AMOGUS " + UserDataUpdate());
                lastStatus = CRCGame.IsInGame;
                CRCGame.UpdateUsers();
                CRCDisplay.UpdateUsers();
            }
        }

        public static void ChangeNick(string nick)
        {
            CRCOptions.Name = nick;
            lastName = nick;
            client.RfcNick(nick);
        }

        public static void Send(string message)
        {
            if ((DateTime.Now - lastMessage).TotalSeconds < 1)
            {
                CRCGame.AddError(CRCStrings.Localize("crc_message_cooldown"));
                CRCDisplay.AddError(CRCStrings.Localize("crc_message_cooldown"));
                return;
            }
            else
            {
                client.SendMessage(SendType.Message, CRCOptions.ChannelProxy(), message);
                CRCDisplay.OnOwnChannelMessage(CRCOptions.Name, message);
                CRCGame.OnChannelMessage(CRCOptions.Name, CRCOptions.GetFaction(), message);
                lastMessage = DateTime.Now;
            }
        }

        public static void SendDeath(string message)
        {
            string nick = CRCStrings.RandomName(CRCOptions.GameFaction);
            client.SendMessage(SendType.Message, CRCOptions.ChannelProxy(), nick + FAKE_DELIM + CRCOptions.GetFaction() + META_DELIM + message);
            CRCDisplay.OnChannelMessage(nick, message);
            CRCGame.OnChannelMessage(nick, CRCOptions.GameFaction, message);
        }

        public static void SendQuery(string nick, string message)
        {
            client.SendMessage(SendType.Message, nick, CRCOptions.GetFaction() + META_DELIM + message);
            CRCDisplay.OnQueryMessage(CRCOptions.Name, nick, message);
            CRCGame.OnQueryMessage(CRCOptions.Name, nick, CRCOptions.GetFaction(), message);
        }

        public static void SendMoney(string nick, string message)
        {
            int amount;
            bool acceptable = int.TryParse(message, out amount);
            if ((DateTime.Now - lastPay).TotalSeconds < 120)
            {
                CRCGame.AddError(CRCStrings.Localize("crc_money_cooldown"));
                CRCDisplay.AddError(CRCStrings.Localize("crc_money_cooldown"));
                return;
            }    
            if (acceptable && amount <=1000000)
            {
                if (amount > CRCGame.ActorMoney)
                {
                    CRCGame.AddError(CRCStrings.Localize("crc_money_none"));
                    CRCDisplay.AddError(CRCStrings.Localize("crc_money_none"));
                    return;
                }
                else 
                {
                    client.SendMessage(SendType.Message, nick, CRCOptions.GetFaction() + " pay " + META_DELIM + amount.ToString());
                    CRCDisplay.OnMoneySent(CRCOptions.Name, nick, amount.ToString());
                    CRCGame.OnMoneySent(CRCOptions.Name, nick, CRCOptions.GetFaction(), amount.ToString());
                    lastPay = DateTime.Now;
                }
            }
            else
            {
                CRCGame.AddError(CRCStrings.Localize("crc_money_toohigh"));
                CRCDisplay.AddError(CRCStrings.Localize("crc_money_toohigh"));
                return;
            }
        }

        public static bool SendReply(string message)
        {
            if (lastQuery != null)
            {
                SendQuery(lastQuery, message);
                return true;
            }
            return false;
        }

        private static string GetMetadata(string message, out string fakeNick, out string faction)
        {
            Match metaMatch = metaRx.Match(message);
            if (metaMatch.Success)
            {
                Match deathMatch = deathRx.Match(metaMatch.Groups[1].Value);
                if (deathMatch.Success)
                {
                    fakeNick = deathMatch.Groups[1].Value;
                    faction = CRCStrings.ValidateFaction(deathMatch.Groups[2].Value);
                    return metaMatch.Groups[2].Value;
                }
                else
                {
                    fakeNick = null;
                    faction = CRCStrings.ValidateFaction(metaMatch.Groups[1].Value);
                    return metaMatch.Groups[2].Value;
                }
            }
            else
            {
                fakeNick = null;
                faction = "actor_stalker";
                return message;
            }
        }



        private static void OnRawMessage(object sender, IrcEventArgs e)
        {
#if DEBUG
            debug?.AddRaw(e.Data.RawMessage);
#endif
        }

        private static void OnCtcpRequest(object sender, CtcpEventArgs e)
        {
            string from = e.Data.Nick;
            switch(e.CtcpCommand.ToUpper())
            {
                case "USERDATA":
                    client.SendMessage(SendType.CtcpReply, CRCOptions.ChannelProxy(), "AMOGUS " + UserDataUpdate());
                    break;
                case "CLIENTINFO":
                    client.SendMessage(SendType.CtcpReply, from, "CLIENTINFO Supported CTCP commands: CLIENTINFO FACTION PING VERSION");
                    break;
                case "PING":
                    client.SendMessage(SendType.CtcpReply, from, "PING " + e.CtcpParameter);
                    break;
                case "VERSION":
                    client.SendMessage(SendType.CtcpReply, from, "VERSION Chernobyl Relay Chat Rebirth " + Application.ProductVersion);
                    break;
            }
        }

        private static void OnCtcpReply(object sender, CtcpEventArgs e)
        {
            string from = e.Data.Nick;
            switch(e.CtcpCommand.ToUpper())
            {
                case "AMOGUS":
                    Match userdataMatch = userdataRx.Match(e.CtcpParameter);
                    if (userdataMatch.Success)
                    {
                        try
                        {
                            userData[from].User = from; // There have been changes here that I am not sure of the consequences, in case of anything going wrong looking here might be worth the time
                            userData[from].Faction = CRCStrings.ValidateFaction(userdataMatch.Groups[2].Value);
                            userData[from].IsInGame = userdataMatch.Groups[3].Value;
                        }
                        catch (Exception ex)
                        { // Fix name and RTCP name mismatch bug
                            if (ex is KeyNotFoundException)
                            {
                                CRCDisplay.AddError($"{from} name and userdata mismatch");
                                CRCGame.AddError($"{from} name and userdata mismatch");
                            }
                            else throw;
                        }
#if DEBUG
                        var debug = JsonConvert.SerializeObject(userData);
                        System.Diagnostics.Debug.WriteLine(debug);
                        System.Diagnostics.Debug.WriteLine(userdataMatch.Groups[1].Value + " " + userdataMatch.Groups[2].Value + " " + userdataMatch.Groups[3].Value);
#endif
                    }
                    CRCGame.UpdateUsers();
                    CRCDisplay.UpdateUsers();
                    break;
            }
        }

        private static void OnConnected(object sender, EventArgs e)
        {
            userData.Clear();
            lastName = CRCOptions.Name;
            lastChannel = CRCOptions.ChannelProxy();
            lastFaction = CRCOptions.GetFaction();
            client.Login(CRCOptions.Name, CRCStrings.Localize("crc_name") + " " + Application.ProductVersion);
            client.RfcJoin(CRCOptions.ChannelProxy());
            CRCDisplay.AddInformation(CRCStrings.Localize("welcome_msg"));
            CRCGame.AddInformation(CRCStrings.Localize("welcome_msg"));
        }

        private static void OnChannelActiveSynced(object sender, IrcEventArgs e)
        {
            userData.Add(CRCOptions.Name, new Userdata { User = CRCOptions.Name, Faction = CRCOptions.GetFaction(), IsInGame = CRCGame.IsInGame.ToString() });
            foreach (ChannelUser user in client.GetChannel(e.Data.Channel).Users.Values)
            {
                if (!userData.ContainsKey(user.Nick))
                    userData.Add(user.Nick, new Userdata { User = user.Nick, Faction = "actor_stalker", IsInGame = "False" });
            }
            client.SendMessage(SendType.CtcpRequest, e.Data.Channel, "USERDATA");
            client.SendMessage(SendType.CtcpReply, e.Data.Channel, "AMOGUS " + UserDataUpdate());
            prevChannel = CRCOptions.Channel;
            CRCDisplay.UpdateUsers();
            CRCGame.UpdateUsers();
        }

        private static void OnDisconnected(object sender, EventArgs e)
        {
            if (retry)
            {
                string message = CRCStrings.Localize("client_reconnecting");
                CRCDisplay.AddInformation(message);
                CRCGame.AddInformation(message);
                client.Connect(CRCOptions.Server, 6667);
            }
        }

        private static void OnTopic(object sender, TopicEventArgs e)
        {
            string message = CRCStrings.Localize("client_topic") + e.Topic;
            CRCDisplay.AddInformation(message);
            CRCGame.AddInformation(message);
        }

        private static void OnTopicChange(object sender, TopicChangeEventArgs e)
        {
            string message = CRCStrings.Localize("client_topic_change") + e.NewTopic;
            CRCDisplay.AddInformation(message);
            CRCGame.AddInformation(message);
        }

        public static void OnSignalLost()
        {
            userData.Clear();
            client.RfcPart(CRCOptions.ChannelProxy());
        }

        public static void OnSignalRestored()
        {
            lastName = CRCOptions.Name;
            prevChannel = lastChannel;
            lastChannel = CRCOptions.ChannelProxy();
            lastFaction = CRCOptions.GetFaction();
            client.RfcJoin(CRCOptions.ChannelProxy());
        }

        private static void OnChannelMessage(object sender, IrcEventArgs e)
        {
            string fakeNick, faction;
            Color nickColor = Color.Black; //Default value for nickname color
            Color messageColor = Color.Black; //Default values for message color 
            string message = GetMetadata(e.Data.Message, out fakeNick, out faction);
            // If some cheeky m8 just sends delimiters, ignore it
            if (message.Length > 0)
            {
                string nick;
                if (fakeNick == null)
                {
                    nick = e.Data.Nick;
                    if (userData.ContainsKey(nick) && CRCOptions.GetFaction() == "actor_isg")
                    {
                        faction = userData[nick].Faction;
                    }
                    else if (userData.ContainsKey(nick) && userData[nick].Faction == "actor_isg" && CRCOptions.GetFaction() != "actor_isg")
                    {
                        faction = "actor_anonymous";
                    }
                    else if (userData.ContainsKey(nick) && userData[nick].Faction != "actor_isg")
                    {
                        faction = userData[nick].Faction;
                    }
                    else
                    {
                        faction = "actor_stalker";
                    }
                }
                else if (CRCOptions.ReceiveDeath && (DateTime.Now - lastDeath).TotalSeconds > CRCOptions.DeathInterval)
                {
                    lastDeath = DateTime.Now;
                    nick = fakeNick; //e.Data.Nick;
                }
                else
                    return;



                Color factionColor = CRCStrings.GetMessageColor(faction);
                if (CRCOptions.BlockList.Contains(nick)) { return; }
                if (CRCOptions.NameColor) { nickColor = factionColor; } // If the name color option is turned on, get the color from the list of colors
                if (CRCOptions.MessageColor) { messageColor = factionColor; }

                

                if (message.Contains(CRCOptions.Name))
                {
                    if (CRCOptions.SoundNotifications)
                        SystemSounds.Asterisk.Play();
                    CRCDisplay.OnHighlightMessage(nick, message);
                    CRCGame.OnHighlightMessage(nick, faction, message);
 
                }
                else
                {
                     CRCDisplay.OnChannelMessage(nick, message, nickColor, messageColor);
                     if (CRCOptions.IngameMessageColor)
                     { // code that deals with the ingame color of the message
                        if (faction != "actor_anonymous") { message = $"%c[255,{factionColor.R},{factionColor.G},{factionColor.B}]" + message; } // Lazy fix for messages being displayed in black text
                     }
                     CRCGame.OnChannelMessage(nick, faction, message);
                }
            }
        }

        private static void OnQueryMessage(object sender, IrcEventArgs e)
        {
            string raw = e.Data.Message;
            bool check = raw.Contains("pay");
            if (check)
            {
                // Metadata should not be used in queries, just throw it out
                string fakeNick, faction;
                string message = GetMetadata(e.Data.Message, out fakeNick, out faction);
                string nick = e.Data.Nick;
                if (CRCOptions.BlockList.Contains(nick)) { CRCDisplay.AddError($"Blocked user {nick} tried paying money"); CRCGame.AddError($"Blocked user {nick} tried paying money");return; }; //Blocked user check thingamajig
                CRCDisplay.OnMoneyRecv(nick, message); 
                CRCGame.OnMoneyRecv(nick, message);
            }
            else
            {
                lastQuery = e.Data.Nick;
                // Metadata should not be used in queries, just throw it out
                string fakeNick, faction;
                string message = GetMetadata(e.Data.Message, out fakeNick, out faction);
                string nick = e.Data.Nick;
                if (userData.ContainsKey(nick) && CRCOptions.GetFaction() == "actor_isg")
                {
                    faction = userData[nick].Faction;
                }
                else if (userData.ContainsKey(nick) && userData[nick].Faction == "actor_isg" && CRCOptions.GetFaction() != "actor_isg")
                {
                    faction = "actor_anonymous";
                }
                else if (userData.ContainsKey(nick) && userData[nick].Faction != "actor_isg")
                {
                    faction = userData[nick].Faction;
                }
                else
                {
                    faction = "actor_stalker";
                }
                if (CRCOptions.BlockList.Contains(nick)) { return; }; //Blocked user check thingamajig
                CRCDisplay.OnQueryMessage(nick, CRCOptions.Name, message);
                CRCGame.OnQueryMessage(nick, CRCOptions.Name, faction, message);
            }
        }

        private static void OnJoin(object sender, JoinEventArgs e)
        {
            if (e.Who != client.Nickname)
            {
                userData.Add(e.Who, new Userdata { User = e.Who, Faction = "actor_stalker", IsInGame = "False" });
                CRCDisplay.UpdateUsers();
                CRCGame.UpdateUsers();
                string message = e.Who + CRCStrings.Localize("client_join");
                CRCDisplay.AddInformation(message);
                CRCGame.AddInformation(message);
            }
            else
            {
                CRCOptions.Name = e.Who;
                string message = CRCStrings.Localize("client_connected") + ChannelToChannelName(CRCOptions.Channel);
                CRCDisplay.AddInformation(message);
                CRCGame.AddInformation(message);
                CRCDisplay.OnConnected();
            }
        }

        private static void OnPart(object sender, PartEventArgs e)
        {
            if (e.Who != CRCOptions.Name)
            {
                userData.Remove(e.Who);
                CRCDisplay.UpdateUsers();
                CRCGame.UpdateUsers();
                string message = e.Who + CRCStrings.Localize("client_part");
                CRCDisplay.AddInformation(message);
                CRCGame.AddInformation(message);
            }
            else
            {
                string message = CRCStrings.Localize("client_own_part") + ChannelToChannelName(prevChannel);
                CRCDisplay.AddInformation(message);
                CRCGame.AddInformation(message);
            }
        }

        private static void OnQuit(object sender, QuitEventArgs e)
        {
            userData.Remove(e.Who);
            CRCDisplay.UpdateUsers();
            CRCGame.UpdateUsers();
            string message = e.Who + CRCStrings.Localize("client_part");
            CRCDisplay.AddInformation(message);
            CRCGame.AddInformation(message);
        }

        private static void OnKick(object sender, KickEventArgs e)
        {
            string victim = e.Whom;
            if (victim == CRCOptions.Name)
            {
                userData.Clear();
                string message = CRCStrings.Localize("client_got_kicked") + e.KickReason;
                CRCDisplay.AddError(message);
                CRCGame.AddError(message);
                CRCDisplay.OnGotKicked();
            }
            else
            {
                userData.Remove(victim);
                string message = victim + CRCStrings.Localize("client_kicked") + e.KickReason;
                CRCDisplay.AddInformation(message);
                CRCGame.AddInformation(message);
            }
            CRCDisplay.UpdateUsers();
            CRCGame.UpdateUsers();
        }

        private static void OnNickChange(object sender, NickChangeEventArgs e)
        {
            string oldNick = e.OldNickname;
            string newNick = e.NewNickname;
            userData.Add(newNick, new Userdata { User = newNick, Faction = "actor_stalker", IsInGame = "False" });
            userData[newNick] = userData[oldNick];
            userData.Remove(oldNick);
            if (newNick != client.Nickname)
            {
                string message = oldNick + CRCStrings.Localize("client_nick_change") + newNick;
                CRCDisplay.AddInformation(message);
                CRCGame.AddInformation(message);
            }
            else
            {
                CRCOptions.Name = newNick;
                string message = CRCStrings.Localize("client_own_nick_change") + newNick;
                CRCDisplay.AddInformation(message);
                CRCGame.AddInformation(message);
            }
            CRCDisplay.UpdateUsers();
            CRCGame.UpdateUsers();
        }

        private static void OnErrorMessage(object sender, IrcEventArgs e)
        {
            string message;
            switch (e.Data.ReplyCode)
            {
                case ReplyCode.ErrorBannedFromChannel:
                    message = CRCStrings.Localize("client_banned");
                    CRCDisplay.AddError(message);
                    CRCGame.AddError(message);
                    break;
                // What's the difference?
                case ReplyCode.ErrorNicknameInUse:
                case ReplyCode.ErrorNicknameCollision:
                    message = CRCStrings.Localize("client_nick_collision");
                    CRCDisplay.AddError(message);
                    CRCGame.AddError(message);
                    break;
                // Don't care
                case ReplyCode.ErrorNoMotd:
                case ReplyCode.ErrorNotRegistered:
                    break;
                case ReplyCode.ErrorNoSuchNickname:
                    message = CRCStrings.Localize("error_no_such_nickname");
                    CRCDisplay.AddError(message);
                    CRCGame.AddError(message);
                    break;
                default:
                    CRCDisplay.AddError(e.Data.Message);
                    CRCGame.AddError(e.Data.Message);
                    break;
            }
        }

        private static string ChannelToChannelName(string rawChannel)
        {
            string Channel = "UNKNOWN";
            if (rawChannel == "#crcr_english") 
                Channel = "Main Channel (Eng)";
            if (rawChannel == "#crcr_english_rp") 
                Channel = "Roleplay Channel (Eng)";
            if (rawChannel == "#crcr_english_shitposting") 
                Channel = "Unmoderated Channel (Eng)";
            if (rawChannel == "#crcr_russian") 
                Channel = "Основной Канал (Русский)";
            if (rawChannel == "#crcr_russian_rp") 
                Channel = "Ролевой Канал (Русский)";
            if (rawChannel == "#crcr_tech_support") 
                Channel = "Tech Support/Техподдержка";
            return Channel;
        }

        private static string UserDataUpdate()
        {
            string userDataUpdate = CRCOptions.Name + "/" + CRCOptions.GetFaction() + "/" + CRCGame.IsInGame.ToString();
            return userDataUpdate;
        }
    }

    public class Userdata
    {
        public string User { get; set; }
        public string Faction { get; set; }
        public string IsInGame { get; set; }
    }
}
