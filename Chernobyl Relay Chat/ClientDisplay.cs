﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Text.RegularExpressions;

namespace Chernobyl_Relay_Chat
{
    public partial class ClientDisplay : Form, ICRCSendable
    {
        private Font mainFont, boldFont, timeFont;
        public static ClientDisplay staticVar = null;

        public ClientDisplay()
        {
            InitializeComponent();
            staticVar = this;
            Text = CRCStrings.Localize("crc_name") + " " + Application.ProductVersion;
            buttonSend.Text = CRCStrings.Localize("display_send");
            buttonOptions.Text = CRCStrings.Localize("display_options");
            CRCClient.lastChannel = CRCOptions.ChannelProxy();
            comboBoxChannel.SelectedIndex = channelToIndex[CRCOptions.Channel];
        }

        private void ClientDisplay_Load(object sender, EventArgs e)
        {
            mainFont = richTextBoxMessages.Font;
            boldFont = new Font(mainFont, FontStyle.Bold);
            timeFont = new Font("Courier New", mainFont.SizeInPoints, FontStyle.Regular);
            if (CRCOptions.DisplaySize != new Size(0, 0))
            {
                Location = CRCOptions.DisplayLocation;
                Size = CRCOptions.DisplaySize;
            }

            AddInformation(CRCStrings.Localize("display_connecting"));
        }

        private void ClientDisplay_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (WindowState == FormWindowState.Normal)
            {
                CRCOptions.DisplayLocation = Location;
                CRCOptions.DisplaySize = Size;
            }
            else
            {
                CRCOptions.DisplayLocation = RestoreBounds.Location;
                CRCOptions.DisplaySize = RestoreBounds.Size;
            }

            CRCClient.Stop();
        }

        private void buttonOptions_Click(object sender, EventArgs e)
        {
            new OptionsForm().ShowDialog();
        }

        private void buttonSend_Click(object sender, EventArgs e)
        {
            string trimmed = textBoxInput.Text.Trim();
            if (trimmed.Length > 0)
            {
                if (trimmed[0] == '/')
                {
                    CRCCommands.ProcessCommand(trimmed, this);
                }
                else if (trimmed.Length > 0)
                {
                    CRCClient.Send(trimmed);
                }
                textBoxInput.Clear();
            }
        }

        public void OnChannelUpdateFromGame(int index)
        {
            comboBoxChannel.SelectedIndex = index;
        }

        private void comboBoxChannel_SelectedIndexChanged(object sender, EventArgs e)
        {
            CRCOptions.Channel = indexToChannel[comboBoxChannel.SelectedIndex];
            CRCClient.OnChannelSwitch();
            CRCGame.OnChannelSwitch();
        }

        private void timerGameCheck_Tick(object sender, EventArgs e)
        {
            CRCGame.GameCheck();
        }

        private void timerGameUpdate_Tick(object sender, EventArgs e)
        {
            CRCGame.GameUpdate();
        }

        private async void timerCheckUpdate_Tick(object sender, EventArgs e)
        {
            bool result = await CRCUpdate.CheckUpdate();
        }

        private void richTextBoxMessages_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            // Apparently safer than just passing the link
            Process.Start("explorer.exe", e.LinkText);
        }


        public void Enable()
        {
            Invoke(() =>
            {
                buttonSend.Enabled = true;
                buttonOptions.Enabled = true;
                comboBoxChannel.Enabled = true;
            });
        }

        public void Disable()
        {
            Invoke(() =>
            {
                buttonSend.Enabled = false;
                buttonOptions.Enabled = false;
                comboBoxChannel.Enabled = false;
            });
        }

        private void AddLinePrefix()
        {
            if (richTextBoxMessages.Lines.Length != 0)
                richTextBoxMessages.AppendText("\r\n");
            if (CRCOptions.ShowTimestamps)
            {
                richTextBoxMessages.SelectionColor = Color.Black;
                richTextBoxMessages.SelectionFont = timeFont;
                richTextBoxMessages.AppendText(DateTime.Now.ToString("[hh:mm:ss] "));
            }
        }

        public void AddLine(string line, Color color)
        {
            Invoke(() =>
            {
                AddLinePrefix();
                richTextBoxMessages.SelectionFont = mainFont;
                richTextBoxMessages.SelectionColor = color;
                richTextBoxMessages.AppendText(line);
            });
        }

        public void AddInformation(string line)
        {
            AddLine(line, Color.DarkBlue);
        }

        public void AddError(string line)
        {
            AddLine(line, Color.Red);
        }

        public void AddMessage(string nick, string message, Color nickColor, Color MessageColor)
        {
            Invoke(() =>
            {

                AddLinePrefix();
                richTextBoxMessages.SelectionFont = boldFont;
                richTextBoxMessages.SelectionColor = nickColor;
                richTextBoxMessages.AppendText(nick + ": ");
                richTextBoxMessages.SelectionFont = mainFont;
                richTextBoxMessages.SelectionColor = MessageColor;
                richTextBoxMessages.AppendText(message);
            });
        }

        public void AddHighlightMessage(string nick, string message)
        {
            Invoke(() =>
            {
                AddMessage(nick, message, Color.Black, Color.Black);
                int start = richTextBoxMessages.GetFirstCharIndexOfCurrentLine();
                int length = richTextBoxMessages.TextLength - start;
                richTextBoxMessages.Select(start, length);
                richTextBoxMessages.SelectionBackColor = Color.Yellow;
                richTextBoxMessages.Select(0, 0);
                richTextBoxMessages.SelectionBackColor = Color.White;
            });
        }

        private void textBoxUsers_TextChanged(object sender, EventArgs e)
        {
            foreach (string user in CRCClient.userData.Keys)
            {
                if (textBoxUsers.Text.Contains(user))
                {
                    if (CRCClient.userData.ContainsKey(user) && CRCClient.userData[user].IsInGame == "True")
                    {
                        textBoxUsers.Select(textBoxUsers.Text.IndexOf(user), user.Length);
                        textBoxUsers.SelectedText = "⦿ " + user;
                    }
                    else
                    {
                        textBoxUsers.Select(textBoxUsers.Text.IndexOf(user), user.Length);
                        textBoxUsers.SelectedText = "⦾ " + user;
                    }

                }
                else return;
            }
            MatchCollection ingame = Regex.Matches(textBoxUsers.Text, "⦿");
            MatchCollection offgame = Regex.Matches(textBoxUsers.Text, "⦾");
            foreach (Match match in ingame)
            {
                textBoxUsers.Select(match.Index, match.Length);
                textBoxUsers.SelectionColor = Color.Green;
            }
            foreach (Match match in offgame)
            {
                textBoxUsers.Select(match.Index, match.Length);
                textBoxUsers.SelectionColor = Color.Red;
            }
        }

        public void UpdateUsers(Dictionary<string, Userdata> users)
        {
            Invoke(() =>
            {
                var nicknames = new List<string>();
                foreach (string user in users.Keys)
                    nicknames.Add(user);
                nicknames.Sort();
                textBoxUsers.Text = string.Join("\r\n", nicknames);
            });
        }

        private void Invoke(Action action)
        {
            base.Invoke(action);
        }

        private readonly Dictionary<string, int> channelToIndex = new Dictionary<string, int>()
        {
            ["#crcr_english"] = 0,
            ["#crcr_english_rp"] = 1,
            ["#crcr_english_shitposting"] = 2,
            ["#crcr_russian"] = 3,
            ["#crcr_russian_rp"] = 4,
            ["#crcr_tech_support"] = 5,
        };

        private readonly Dictionary<int, string> indexToChannel = new Dictionary<int, string>()
        {
            [0] = "#crcr_english",
            [1] = "#crcr_english_rp",
            [2] = "#crcr_english_shitposting",
            [3] = "#crcr_russian",
            [4] = "#crcr_russian_rp",
            [5] = "#crcr_tech_support",
        };
    }
}
