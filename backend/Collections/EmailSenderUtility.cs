using System;
using System.Linq;
using System.Net;
using System.Net.Mail;

namespace Collections
{
    public class EmailSenderUtility
    {
        private const string SMTP_ADDRESS = ""; // TODO: read from configuration
        private const int SMTP_PORT = 587;
        private const string SMTP_USER = ""; // TODO: read from configuration
        private const string SMTP_PASSWORD = ""; // TODO: read from configuration
        public const string SENDER_ADDRESS = ""; // TODO: read from configuration
        private const string EMAIL_TEXT = @"<!doctype html> 
<html dir=""rtl""> 
  <head> 
    <meta name=""viewport"" content=""width=device-width""> 
    <meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8""> 
    <title>فعال سازی</title> 
    <style> 
    @media only screen and (max-width: 620px) { 
      table[class=body] h1 { 
        font-size: 28px !important; 
        margin-bottom: 10px !important; 
      } 
      table[class=body] p, 
            table[class=body] ul, 
            table[class=body] ol, 
            table[class=body] td, 
            table[class=body] span, 
            table[class=body] a { 
        font-size: 16px !important; 
      } 
      table[class=body] .wrapper, 
            table[class=body] .article { 
        padding: 10px !important; 
      } 
      table[class=body] .content { 
        padding: 0 !important; 
      } 
      table[class=body] .container { 
        padding: 0 !important; 
        width: 100% !important; 
      } 
      table[class=body] .main { 
        border-left-width: 0 !important; 
        border-radius: 0 !important; 
        border-right-width: 0 !important; 
      } 
      table[class=body] .btn table { 
        width: 100% !important; 
      } 
      table[class=body] .btn a { 
        width: 100% !important; 
      } 
      table[class=body] .img-responsive { 
        height: auto !important; 
        max-width: 100% !important; 
        width: auto !important; 
      } 
    } 
    @media all { 
      .ExternalClass { 
        width: 100%; 
      } 
      .ExternalClass, 
            .ExternalClass p, 
            .ExternalClass span, 
            .ExternalClass font, 
            .ExternalClass td, 
            .ExternalClass div { 
        line-height: 100%; 
      } 
      .apple-link a { 
        color: inherit !important; 
        font-family: inherit !important; 
        font-size: inherit !important; 
        font-weight: inherit !important; 
        line-height: inherit !important; 
        text-decoration: none !important; 
      } 
      .btn-primary table td:hover { 
        background-color: #34495e !important; 
      } 
      .btn-primary a:hover { 
        background-color: #34495e !important; 
        border-color: #34495e !important; 
      } 
    } 
    </style> 
  </head> 
  <body class="""" style=""background-color: #f6f6f6; font-family: sans-serif; -webkit-font-smoothing: antialiased; font-size: 14px; line-height: 1.4; margin: 0; padding: 0; -ms-text-size-adjust: 100%; -webkit-text-size-adjust: 100%;""> 
    <table border=""0"" cellpadding=""0"" cellspacing=""0"" class=""body"" style=""border-collapse: separate; mso-table-lspace: 0pt; mso-table-rspace: 0pt; width: 100%; background-color: #f6f6f6;""> 
      <tr> 
        <td style=""font-family: sans-serif; font-size: 14px; vertical-align: top;"">&nbsp;</td> 
        <td class=""container"" style=""font-family: sans-serif; font-size: 14px; vertical-align: top; display: block; Margin: 0 auto; max-width: 580px; padding: 10px; width: 580px;""> 
          <div class=""content"" style=""box-sizing: border-box; display: block; Margin: 0 auto; max-width: 580px; padding: 10px;""> 
  
            <!-- START CENTERED WHITE CONTAINER --> 
            <span class=""preheader"" style=""color: transparent; display: none; height: 0; max-height: 0; max-width: 0; opacity: 0; overflow: hidden; mso-hide: all; visibility: hidden; width: 0;"">This is preheader text. Some clients will show this text as a preview.</span> 
            <table class=""main"" style=""border-collapse: separate; mso-table-lspace: 0pt; mso-table-rspace: 0pt; width: 100%; background: #ffffff; border-radius: 3px;""> 
  
              <!-- START MAIN CONTENT AREA --> 
              <tr> 
                <td class=""wrapper"" style=""font-family: sans-serif; font-size: 14px; vertical-align: top; box-sizing: border-box; padding: 20px;""> 
                  <table border=""0"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse: separate; mso-table-lspace: 0pt; mso-table-rspace: 0pt; width: 100%;""> 
                    <tr> 
                      <td style=""font-family: sans-serif; font-size: 14px; vertical-align: top;""> 
                        <p style=""font-family: sans-serif; font-size: 14px; font-weight: normal; margin: 0; Margin-bottom: 15px;"">سلام,</p> 
                        <p style=""font-family: sans-serif; font-size: 14px; font-weight: normal; margin: 0; Margin-bottom: 15px;"">این ایمل به منظور فعال سازی حساب کاربری شما در بازی {0} ارسال شده است. عبارت درون کادر زیر کد امنیتی شما می باشد.</p> 
                        <table border=""0"" cellpadding=""0"" cellspacing=""0""  style=""border-collapse: separate; mso-table-lspace: 0pt; mso-table-rspace: 0pt; width: 100%; box-sizing: border-box;""> 
                          <tbody> 
                            <tr> 
                              <td align=""left"" style=""font-family: sans-serif; font-size: 14px; vertical-align: top; padding-bottom: 15px;""> 
                                <table border=""0"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse: separate; mso-table-lspace: 0pt; mso-table-rspace: 0pt; width: auto;""> 
                                  <tbody> 
                                    <tr> 
                                      <td style=""font-family: sans-serif; font-size: 14px; vertical-align: top; border-radius: 5px; text-align: center;""> <p  style=""display: inline-block; color: #00000; background-color: #ffffff; border: solid 2px #3498db; border-radius: 5px; box-sizing: border-box; text-decoration: none; font-size: 14px; font-weight: bold; margin: 0; padding: 12px 25px; text-transform: capitalize; border-color: #3498db;"">{1}</p> </td> 
                                    </tr> 
                                  </tbody> 
                                </table> 
                              </td> 
                            </tr> 
                          </tbody> 
                        </table> 
                         
                        <p style=""font-family: sans-serif; font-size: 14px; font-weight: normal; margin: 0; Margin-bottom: 15px;""></p> 
                      </td> 
                    </tr> 
                  </table> 
                </td> 
              </tr> 
  
            <!-- END MAIN CONTENT AREA --> 
            </table> 
  
            <!-- START FOOTER --> 
            <div class=""footer"" style=""clear: both; Margin-top: 10px; text-align: center; width: 100%;""> 
              <table border=""0"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse: separate; mso-table-lspace: 0pt; mso-table-rspace: 0pt; width: 100%;""> 
       
                <tr> 
     
                </tr> 
              </table> 
            </div> 
            <!-- END FOOTER --> 
  
          <!-- END CENTERED WHITE CONTAINER --> 
          </div> 
        </td> 
        <td style=""font-family: sans-serif; font-size: 14px; vertical-align: top;"">&nbsp;</td> 
      </tr> 
    </table> 
  </body> 
</html> ";

        private static Random rnd;

        public static void SendEmail(string senderEmail, string receiver, string subject, string bodyParam)
        {
            senderEmail = senderEmail.ToLower();
            var mail = new MailMessage(senderEmail, receiver);
            var client = new SmtpClient(SMTP_ADDRESS, SMTP_PORT)
            {
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                EnableSsl = true,
                Credentials = new NetworkCredential(SMTP_USER, SMTP_PASSWORD)
            };
            //mail.IsBodyHtml = true;
            mail.Subject = subject;
            mail.Body = EMAIL_TEXT.Replace("{1}", bodyParam);
#if MASTER
            client.Send(mail);
#else
            Console.WriteLine($"######## {bodyParam}");
#endif
        }

        public static string RandomString(int length)
        {
            if (rnd == null)
                rnd = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[rnd.Next(s.Length)]).ToArray());
        }
    }
}
