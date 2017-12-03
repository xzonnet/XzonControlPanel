using System;
using System.Net;
using System.Net.Mail;
using XzonControlPanel.Config;

namespace XzonControlPanel.Logging
{
    public static class EmailHelper
    {
        private static readonly string AlertEmailAddress = "AlertEmailAddress".FromConfig();
        private static readonly string AlertEmailPassword = "AlertEmailPassword".FromConfig();
        private static readonly string AlertEmailSmtpServer = "AlertEmailSmtpServer".FromConfig() ?? "smtp.gmail.com";
        private static readonly int AlertEmailSmtpPort = "AlertEmailSmtpPort".FromConfig().ToInt() ?? 587;
        private static readonly bool AlertEmailSmtpEnableSsl = "AlertEmailSmtpEnableSsl".FromConfig().ToBool() ?? true;

        public static void SendEmail(string address, string text, string title = "Mining Rig Overheat")
        {
            try
            {
                if (string.IsNullOrEmpty(AlertEmailAddress))
                {
                    Log.Warning("Tried to send an email but no AlertEmailAddress was configured.");
                    return;
                }

                var fromAddress = new MailAddress(AlertEmailAddress, "Mining Rig Monitor");

                string subject = title;
                string body = text;

                var smtp = new SmtpClient
                {
                    Host = AlertEmailSmtpServer,
                    Port = AlertEmailSmtpPort,
                    EnableSsl = AlertEmailSmtpEnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(AlertEmailAddress, AlertEmailPassword)
                };
                using (var message =
                    new MailMessage
                    {
                        From = fromAddress,
                        Subject = subject,
                        Body = body
                    })
                {
                    foreach (var add in address.Split(','))
                    {
                        message.To.Add(add);
                    }

                    smtp.Send(message);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, true);
            }
        }

        public static void SendEmailWithAttachment(string address, string text, string path, string title = "Mining Rig Overheat")
        {
            try
            {
                if (string.IsNullOrEmpty(AlertEmailAddress))
                {
                    Log.Warning("Tried to send an email but no AlertEmailAddress was configured.");
                    return;
                }

                var fromAddress = new MailAddress(AlertEmailAddress, "Mining Rig Monitor");

                string subject = title;
                string body = text;

                var smtp = new SmtpClient
                {
                    Host = AlertEmailSmtpServer,
                    Port = AlertEmailSmtpPort,
                    EnableSsl = AlertEmailSmtpEnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(AlertEmailAddress, AlertEmailPassword)
                };
                using (var message =
                    new MailMessage
                    {
                        From = fromAddress,
                        Subject = subject,
                        Body = body
                    })
                {
                    foreach (var add in address.Split(','))
                    {
                        message.To.Add(add);
                    }

                    message.Attachments.Add(new Attachment(path));

                    smtp.Send(message);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, true);
            }
        }
    }
}
