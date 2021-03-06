﻿using MimeKit;
using System;
using System.Collections.Generic;
using System.Data.Linq;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheMarketingPlatform.Core.JSON;
using TheMarketingPlatform.Core.Secure;
using TheMarketingPlatform.Mail;


namespace TheMarketingPlatform.Database
{
    /// <summary>
    /// Handles database connections
    /// </summary>
    public class Controller
    {
        public List<IMailClientSettings> MailClientSettings { get; private set; }
        public int MailAccountsCount => databaseContext.GetTable<MailAccount>().Count();

        public List<MailAccount> MailAccounts => databaseContext.GetTable<MailAccount>().ToList();

        public List<Ticket> Tickets => CreateTicketsFromDatabase();


        MainDatabaseContext databaseContext;

        public Controller(string connection)
        {
            databaseContext = new MainDatabaseContext(connection);
            MailClientSettings = new List<IMailClientSettings>();
            GetMailSettings();
        }

        public Mail GetLastMessage() =>
            databaseContext.GetTable<Mail>().OrderByDescending(m => m.TimeStamp).FirstOrDefault();

        public int Insert(MimeMessage mimeMessage)
        {
            var mail = new Mail()
            {
                Body = mimeMessage.TextBody,
                Subject = mimeMessage.Subject,
                TimeStamp = mimeMessage.Date
            };

            databaseContext.GetTable<Mail>().InsertOnSubmit(mail);
            databaseContext.SubmitChanges();

            var mailAddresses = new List<MailAddress>();
            var dictonary = new Dictionary<AddressType, InternetAddressList>
            {
                { AddressType.From, mimeMessage.From },
                { AddressType.To, mimeMessage.To },
                { AddressType.CC, mimeMessage.Cc },
                { AddressType.BCC, mimeMessage.Bcc }
            };

            foreach (var type in dictonary)
            {
                foreach (MailboxAddress adress in type.Value)
                {
                    mailAddresses.Add(new MailAddress()
                    {
                        Adress = adress.Address,
                        MailId = mail.Id,
                        Type = new[] { (byte)type.Key }
                    });
                }
            }

            databaseContext.GetTable<MailAddress>().InsertAllOnSubmit(mailAddresses);
            databaseContext.SubmitChanges();

            return mail.Id;
        }
        public int Insert(Response response, int messageid)
        {
            var luisResponse = new LuisResponse()
            {
                MailId = messageid,
                TimeStamp = DateTimeOffset.Now
            };

            databaseContext.GetTable<LuisResponse>().InsertOnSubmit(luisResponse);
            databaseContext.SubmitChanges();

            var entities = new List<LuisEntity>();
            foreach (var entity in response.Entities)
            {
                entities.Add(new LuisEntity()
                {
                    LuisResponseId = luisResponse.Id,
                    Entity = entity.EntityName,
                    StartIndex = entity.StartIndex,
                    EndIndex = entity.EndIndex,
                    Score = entity.Score,
                    Type = entity.Type
                });
            }

            databaseContext.GetTable<LuisEntity>().InsertAllOnSubmit(entities);

            var intents = new List<LuisIntent>();

            foreach (var intent in response.Intents)
            {
                intents.Add(new LuisIntent()
                {
                    LuisResponseId = luisResponse.Id,
                    Intent = intent.IntentName,
                    Score = intent.Score,
                    IsTopScore = intent.IntentName == response.TopScoringIntent.IntentName
                });
            }

            databaseContext.GetTable<LuisIntent>().InsertAllOnSubmit(intents);
            databaseContext.SubmitChanges();

            return luisResponse.Id;
        }

        private void GetMailSettings()
        {
            foreach (var setting in databaseContext.GetTable<MailAccount>().Where(
                s => s.Type == new byte[] { (byte)MailClientType.Imap }))
            {
                MailClientSettings.Add(new ImapClientSetting()
                {
                    Host = setting.Host,
                    Password = setting.Password,
                    Port = setting.Port,
                    UserName = setting.Username,
                    UseSsl = setting.UseSsl,
                    Folder = GetFolder(setting.MailAccountFolder.ToList())

                });
            }
        }

        public void AddMailAccounts(List<Core.JSON.MailAccount> list)
        {
            var table = databaseContext.GetTable<MailAccount>();

            foreach (var account in list)
            {
                Enum.TryParse(account.Type, out MailClientType type);
                var mailAccount = new MailAccount()
                {
                    Id = account.Id,
                    Host = account.Host,
                    Password = account.Password,
                    Port = account.Port,
                    Type = new byte[] { (byte)type },
                    Username = account.Username,
                    UseSsl = account.SSL
                };

                var record = table.FirstOrDefault(a => a.Id == mailAccount.Id);

                if (record == null)
                    table.InsertOnSubmit(mailAccount);
                else
                    record = mailAccount;
            }

            databaseContext.SubmitChanges();
        }


        public void RemoveMailAccounts(List<Core.JSON.MailAccount> list)
        {
            var table = databaseContext.GetTable<MailAccount>();

            foreach (var account in list)
            {
                Enum.TryParse(account.Type, out MailClientType type);
                var mailAccount = new MailAccount()
                {
                    Id = account.Id,
                    Host = account.Host,
                    Password = account.Password,
                    Port = account.Port,
                    Type = new byte[] { (byte)type },
                    Username = account.Username,
                    UseSsl = account.SSL
                };

                var record = table.FirstOrDefault(a => a.Id == mailAccount.Id);

                if (record == null)
                    continue;
                else
                    table.DeleteOnSubmit(mailAccount);
            }

            databaseContext.SubmitChanges();
        }


        private List<string> GetFolder(List<MailAccountFolder> list)
        {
            var tmp = new List<string>();

            foreach (var folder in list)
                tmp.Add(folder.Folder);

            return tmp;
        }


        private List<Ticket> CreateTicketsFromDatabase()
        {
            var list = new List<Ticket>();
            foreach (var mail in databaseContext.GetTable<Mail>())
            {
                var luisIntent = mail.LuisResponse.OrderByDescending(
                    l => l.TimeStamp).FirstOrDefault()?.LuisIntent.Where(
                        i => i.IsTopScore).FirstOrDefault();

                if (luisIntent == null)
                    continue;

                list.Add(new Ticket()
                {
                    Id = mail.Id,
                    Body = mail.Body,
                    From = mail.MailAddress.FirstOrDefault(
                        m => m.Type.ToArray()[0] == (byte)AddressType.From).Adress,
                    Date = mail.TimeStamp.ToString("MM.dd.yyyy hh:mm"),
                    Intent = luisIntent.Intent,
                    Score = luisIntent.Score
                });
            }

            return list;
        }
    }
}
