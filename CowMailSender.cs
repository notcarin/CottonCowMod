using System;
using System.Collections.Generic;
using HarmonyLib;
using TotS.Mail;

namespace CottonCowMod
{
    /// <summary>
    /// Creates and delivers in-game mail notifications when cows are unlocked.
    /// Uses raw English text as localization keys — Articy localization returns the
    /// key itself when not found, so our text displays directly.
    /// </summary>
    public static class CowMailSender
    {
        /// <summary>
        /// Creates a "need more space" letter from the given NPC when the player reaches
        /// level 11 but doesn't have Garden Area 5 yet. Teases a gift without spoiling the cow.
        /// Tracked via unlock string so it's only sent once per NPC.
        /// </summary>
        public static void QueueNeedSpaceLetter(string ownerNPC)
        {
            try
            {
                string unlockString = $"COTTONCOWMOD_NeedSpaceLetter_{ownerNPC}";

                if (!Singleton<TotS.Unlock.UnlockManager>.Exists)
                    return;
                if (Singleton<TotS.Unlock.UnlockManager>.Instance.ContainsUnlockString(unlockString))
                    return;

                if (MailManager.Instance == null)
                {
                    CottonCowModPlugin.Log.LogWarning(
                        "CowMailSender: MailManager not available, cannot send need-space letter.");
                    return;
                }

                string title;
                string content;
                string signoff;

                if (ownerNPC == "Young_Tom_Cotton")
                {
                    title = "A Secret";
                    content = "Hullo Hobbit!\n" +
                              "I've got a surprise for you but I can't give it to you yet. " +
                              "You need more garden space first.\n" +
                              "Please hurry up and expand your garden, " +
                              "I can't keep this secret much longer!";
                    signoff = "Your friend,";
                }
                else
                {
                    title = "A Word From the Farm";
                    content = "Hullo Hobbit!\n" +
                              "I've got something for you — been meaning to bring it up " +
                              "for a while now.\n" +
                              "Trouble is, you'd need a bit more room " +
                              "in that garden of yours first. Come see me once you've " +
                              "got the space sorted.";
                    signoff = "Your neighbour,";
                }

                var mailParams = new MailManager.MailParams(
                    mailCategoryID: "Information",
                    mailTitleID: title,
                    mailSenderID: ownerNPC,
                    mailContentID: content,
                    mailSignoffID: signoff,
                    mailSentDate: "",
                    mailSentSeason: "",
                    mailID: $"COTTONCOWMOD_NeedSpaceLetter_{ownerNPC}"
                );

                var mailItem = MailManager.MakeGenericMail(MailState.ActiveUnread, mailParams);

                var mailMgr = MailManager.Instance;
                var activeMail = Traverse.Create(mailMgr)
                    .Field("m_ActiveMail").GetValue<List<MailManager.MailItem>>();

                if (activeMail == null)
                {
                    CottonCowModPlugin.Log.LogError(
                        "CowMailSender: Could not access m_ActiveMail for need-space letter.");
                    return;
                }

                activeMail.Add(mailItem);
                Traverse.Create(mailMgr).Method("DoMailChanged").GetValue();

                Singleton<TotS.Unlock.UnlockManager>.Instance.AddUnlockString(unlockString);

                CottonCowModPlugin.Log.LogInfo(
                    $"CowMailSender: Delivered need-space letter from {ownerNPC}.");
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError(
                    $"CowMailSender: QueueNeedSpaceLetter exception: {ex}");
            }
        }

        /// <summary>
        /// Creates a cow letter from the given NPC and adds it to the player's active mail.
        /// Called during deferred activation (on the morning the cow appears).
        /// </summary>
        public static void QueueCowLetter(string ownerNPC)
        {
            try
            {
                if (MailManager.Instance == null)
                {
                    CottonCowModPlugin.Log.LogWarning(
                        "CowMailSender: MailManager not available, cannot send letter.");
                    return;
                }

                string title;
                string content;
                string signoff;

                if (ownerNPC == "Young_Tom_Cotton")
                {
                    title = "A Gift for You!";
                    content = "Hullo Hobbit!\n" +
                              "One of our cows won't stop mooing at your gate! " +
                              "Da says she's yours now.\n" +
                              "There's a feeding trough in your garden storage — " +
                              "she likes vegetables, grains, and apples. " +
                              "Feed her every day and she'll have milk for you!";
                    signoff = "Your friend,";
                }
                else
                {
                    title = "A Surprise for You";
                    content = "Hullo Hobbit!\n" +
                              "One of our cows has taken a shine to you. " +
                              "I reckon she'd be happier in your garden.\n" +
                              "There's a feeding trough in your garden storage — " +
                              "she'll need vegetables, grains, or apples daily. " +
                              "Don't forget to milk her!";
                    signoff = "Your neighbour,";
                }

                var mailParams = new MailManager.MailParams(
                    mailCategoryID: "Information",
                    mailTitleID: title,
                    mailSenderID: ownerNPC,
                    mailContentID: content,
                    mailSignoffID: signoff,
                    mailSentDate: "",
                    mailSentSeason: "",
                    mailID: $"COTTONCOWMOD_CowLetter_{ownerNPC}"
                );

                var mailItem = MailManager.MakeGenericMail(MailState.ActiveUnread, mailParams);

                var mailMgr = MailManager.Instance;
                var activeMail = Traverse.Create(mailMgr)
                    .Field("m_ActiveMail").GetValue<List<MailManager.MailItem>>();

                if (activeMail == null)
                {
                    CottonCowModPlugin.Log.LogError(
                        "CowMailSender: Could not access m_ActiveMail list.");
                    return;
                }

                activeMail.Add(mailItem);

                // Notify the mail UI to update (shows mailbox indicator)
                Traverse.Create(mailMgr).Method("DoMailChanged").GetValue();

                CottonCowModPlugin.Log.LogInfo(
                    $"CowMailSender: Delivered letter from {ownerNPC} to active mail.");
            }
            catch (Exception ex)
            {
                CottonCowModPlugin.Log.LogError($"CowMailSender: QueueCowLetter exception: {ex}");
            }
        }
    }
}
