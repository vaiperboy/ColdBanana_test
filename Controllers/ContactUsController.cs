using ColdBanana.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using System.Net;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Sync;

namespace ColdBanana.Controllers {
    public class ContactUsController : UmbracoApiController {

        private readonly SmtpSettings _smtpSettings;
        private readonly IPublishedContentQuery _contentQuery;
        private int messageMaxLength { get; set; } //I know, a configuration class/manager would be better


        public ContactUsController(IOptions<SmtpSettings> smtpSettings, IPublishedContentQuery contentQuery) {
            _smtpSettings = smtpSettings.Value; //for SMTP settings done via Startup.cs
            _contentQuery = contentQuery; //for getting the property from document


            var contactUsPage = _contentQuery.ContentAtRoot().FirstOrDefault(x => x.ContentType.Alias == "contactUsPage"); //get page
            if (contactUsPage != null) {
                _smtpSettings.Recipient = contactUsPage.Value<string>("recipientEmailAddress"); //get property
                messageMaxLength = contactUsPage.Value<int>("messageMaxLength"); //get property
            }
        }


        [HttpPost]
        public ActionResult SendEmail([FromBody] ContactFormModel model) {
            // Check if message is too long
            if (model.Message.Length > messageMaxLength) {
                ModelState.AddModelError("Message", $"Message cannot be more than {messageMaxLength} characters long.");
            }


            if (!ModelState.IsValid) { //if the inputs do not match the model specs
                return BadRequest(ModelState);
            }

            var (emailSubject, bodyTemplate) = GetEmailTemplate(model);
            if (string.IsNullOrWhiteSpace(bodyTemplate) || string.IsNullOrWhiteSpace(emailSubject)) {
                return StatusCode(500, "Email subject or body template not found.");
            }


            //start the smtp
            try {
                using (var client = new SmtpClient(_smtpSettings.Host, _smtpSettings.Port)) {
                    client.EnableSsl = _smtpSettings.EnableSsl;
                    client.Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password);

                    
                    if (_smtpSettings.Recipient == null) throw new Exception("Recipient email does not exist. Check Umbraco configuration");
                    var mailMessage = new MailMessage {
                        From = new MailAddress(model.Email, model.Name),
                        Subject = emailSubject, 
                        Body = bodyTemplate, 
                        IsBodyHtml = true // Send as html body
                    };

                    mailMessage.ReplyToList.Add(new MailAddress(model.Email, model.Name)); // Adding Reply-To header 
                    mailMessage.To.Add(_smtpSettings.Recipient);


                    client.Send(mailMessage);
                }

                //should probably put better messages
                return Ok("Email sent successfully");
            } catch (Exception ex) {
                //log exception somewhere, still did not get into that in Ubranco

                return StatusCode(500, $"Internal server error"); //bad practice to expose error
            }
        }

        //probably should be somewhere else, but it is fine
        private (string? Subject, string? BodyTemplate) GetEmailTemplate(ContactFormModel model) {
            var contactUsPage = _contentQuery.ContentAtRoot()
                                             .FirstOrDefault(x => x.ContentType.Alias == "contactUsPage");
            if (contactUsPage == null) {
                throw new Exception("Configuration not found.");
            }

            // Fetch the emailTemplate content node directly from the contactUsPage Content Picker property
            var emailTemplateReference = contactUsPage.Value<IPublishedContent>("emailTemplate");

            if (emailTemplateReference == null) {
                throw new Exception("Email template reference not found.");
            }

            string? emailSubject = emailTemplateReference.Value<string>("subject");
            string? bodyTemplate = emailTemplateReference.Value<string>("bodyTemplate");

            // Replace placeholders in the body template
            bodyTemplate = bodyTemplate?.Replace("{{Name}}", model.Name)
                                        .Replace("{{Email}}", model.Email)
                                        .Replace("{{Message}}", model.Message);

            return (emailSubject, bodyTemplate);
        }


    }
}
