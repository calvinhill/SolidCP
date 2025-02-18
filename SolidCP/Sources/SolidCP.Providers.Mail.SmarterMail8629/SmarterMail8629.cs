// Copyright (c) 2016, SolidCP
// SolidCP is distributed under the Creative Commons Share-alike license
// 
// SolidCP is a fork of WebsitePanel:
// Copyright (c) 2015, Outercurve Foundation.
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
//
// - Redistributions of source code must  retain  the  above copyright notice, this
//   list of conditions and the following disclaimer.
//
// - Redistributions in binary form  must  reproduce the  above  copyright  notice,
//   this list of conditions  and  the  following  disclaimer in  the documentation
//   and/or other materials provided with the distribution.
//
// - Neither  the  name  of  the  Outercurve Foundation  nor   the   names  of  its
//   contributors may be used to endorse or  promote  products  derived  from  this
//   software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING,  BUT  NOT  LIMITED TO, THE IMPLIED
// WARRANTIES  OF  MERCHANTABILITY   AND  FITNESS  FOR  A  PARTICULAR  PURPOSE  ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
// ANY DIRECT, INDIRECT, INCIDENTAL,  SPECIAL,  EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO,  PROCUREMENT  OF  SUBSTITUTE  GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)  HOWEVER  CAUSED AND ON
// ANY  THEORY  OF  LIABILITY,  WHETHER  IN  CONTRACT,  STRICT  LIABILITY,  OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE)  ARISING  IN  ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using SolidCP.Providers.Common;
using SolidCP.Server.Utils;
using Microsoft.Win32;
using FileUtils = SolidCP.Providers.Utils.FileUtils;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;

namespace SolidCP.Providers.Mail
{
    class SmarterMail8629 : HostingServiceProviderBase, IMailServer
    {

        #region Public Properties

        protected string AdminUsername
        {
            get { return ProviderSettings["AdminUsername"]; }
        }

        protected string AdminPassword
        {
            get { return ProviderSettings["AdminPassword"]; }
        }

        protected bool ImportDomainAdmin
        {
            get
            {
                bool res;
                bool.TryParse(ProviderSettings[Constants.ImportDomainAdmin], out res);
                return res;
            }
        }

        protected bool InheritDomainDefaultLimits
        {
            get
            {
                bool res;
                bool.TryParse(ProviderSettings[Constants.InheritDomainDefaultLimits], out res);
                return res;
            }
        }

        protected bool EnableDomainAdministrators
        {
            get
            {
                bool res;
                bool.TryParse(ProviderSettings[Constants.EnableDomainAdministrators], out res);
                return res;
            }
        }

        protected string DomainsPath
        {
            get { return FileUtils.EvaluateSystemVariables(ProviderSettings["DomainsPath"]); }
        }

        protected string ServerIP
        {
            get
            {
                string val = ProviderSettings["ServerIPAddress"];
                if (String.IsNullOrEmpty(val))
                    return "127.0.0.1";

                string ip = val.Trim();
                if (ip.IndexOf(";") > -1)
                {
                    string[] ips = ip.Split(';');
                    ip = String.IsNullOrEmpty(ips[1]) ? ips[0] : ips[1]; // get internal IP part
                }
                return ip;
            }
        }

        protected string ServiceUrl
        {
            get { return ProviderSettings["ServiceUrl"]; }
        }
        #endregion

        #region Constants
        public const string SYSTEM_DOMAIN_ADMIN = "system.domain.admin";
        public const string SYSTEM_CATCH_ALL = "system.catch.all";
        #endregion

        #region Class

        public class AuthToken
        {
            public string emailAddress { get; set; }
            public bool changePasswordNeeded { get; set; }
            public bool displayWelcomeWizard { get; set; }
            public bool isAdmin { get; set; }
            public bool isDomainAdmin { get; set; }
            public bool isLicensed { get; set; }
            public string autoLoginToken { get; set; }
            public string autoLoginUrl { get; set; }
            public string localeId { get; set; }
            public bool isImpersonating { get; set; }
            public bool canViewPasswords { get; set; }
            public string accessToken { get; set; }
            public string refreshToken { get; set; }
            public DateTime accessTokenExpiration { get; set; }
            public string username { get; set; }
            public bool success { get; set; }
            public HttpStatusCode resultCode { get; set; }
            public string message { get; set; }
            public string debugInfo { get; set; }
        }
        public class Domain
        {
            public string name { get; set; }
            public string path { get; set; }
            public string hostname { get; set; }
            public bool isEnabled { get; set; }
            public int userCount { get; set; }
            public int userLimit { get; set; }
            public int aliasCount { get; set; }
            public int aliasLimit { get; set; }
            public int domainAliasCount { get; set; }
            public int listCount { get; set; }
            public int listLimit { get; set; }
            public int size { get; set; }
            public int maxSize { get; set; }
            public int fileStorageSize { get; set; }
            public int sizeMb { get; set; }
            public int fileStorageSizeMb { get; set; }
            public string imgLink { get; set; }
            public string status { get; set; }
        }

        #endregion

        #region Connection

        public async Task<AuthToken> GetAccessToken()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            HttpClient client = new HttpClient();
            var loginData = new
            {
                username = AdminUsername,
                password = AdminPassword
            };
            var loginDatajson = JsonConvert.SerializeObject(loginData);
            var authinput_post = new StringContent(loginDatajson, Encoding.UTF8, "application/json");
            var authurl = ServiceUrl + "/api/v1/auth/authenticate-user";

            var authresponse = await client.PostAsync(authurl, authinput_post);
            authresponse.EnsureSuccessStatusCode();
            var authresult = await authresponse.Content.ReadAsStringAsync();
            AuthToken authdata = JsonConvert.DeserializeObject<AuthToken>(authresult);

            return authdata;
        }

        public async Task<AuthToken> GetDomainAccessToken(string domain)
        {
            Log.WriteStart("GetAccessToken");
            AuthToken authdata = new AuthToken();
            var info = "";

            try
            {
                AuthToken authToken = await GetAccessToken();

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

                HttpClient client = new HttpClient();
                var domainData = new { };
                var domainDatajson = JsonConvert.SerializeObject(domainData);

                info += authToken.accessToken;

                var authinput_post = new StringContent(domainDatajson, Encoding.UTF8, "application/json");
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + authToken.accessToken);
                var authurl = ServiceUrl + "/api/v1/settings/sysadmin/manage-domain/" + domain;
                info += " " + authurl;

                var authresponse = await client.PostAsync(authurl, authinput_post);
                authresponse.EnsureSuccessStatusCode();
                var authresult = await authresponse.Content.ReadAsStringAsync();
                dynamic data = JsonConvert.DeserializeObject<object>(authresult);

                authdata.accessToken = data["impersonateAccessToken"].ToString();
                authdata.refreshToken = data["impersonateRefreshToken"].ToString();
                authdata.accessTokenExpiration = Convert.ToDateTime(data["impersonateAccessTokenExpiration"]);
                authdata.username = data["username"].ToString();
                authdata.emailAddress = data["email"].ToString();
                authdata.success = Convert.ToBoolean(data["success"]);
            }
            catch (Exception e)
            {
                Log.WriteError("Exec Domain", new Exception(info));
            }

            return authdata;
        }

        public async Task<AuthToken> GetUserAccessToken(string email)
        {
            Log.WriteStart("GetUserAccessToken");

            AuthToken authToken = await GetAccessToken();

            Log.WriteStart("GetUserAccessToken - GetAccessToken");

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            HttpClient client = new HttpClient();
            var UserData = new
            {
                email = email
            };

            var UserDatajson = JsonConvert.SerializeObject(UserData);
            var authinput_post = new StringContent(UserDatajson, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + authToken.accessToken);
            var authurl = ServiceUrl + "/api/v1/settings/domain/impersonate-user";

            var authresponse = await client.PostAsync(authurl, authinput_post);
            authresponse.EnsureSuccessStatusCode();
            var authresult = await authresponse.Content.ReadAsStringAsync();
            dynamic data = JsonConvert.DeserializeObject<object>(authresult);

            AuthToken authdata = new AuthToken();

            authdata.accessToken = data["impersonateAccessToken"].ToString();
            authdata.refreshToken = data["impersonateRefreshToken"].ToString();
            authdata.accessTokenExpiration = Convert.ToDateTime(data["impersonateAccessTokenExpiration"]);
            authdata.username = data["username"].ToString();
            authdata.emailAddress = data["email"].ToString();
            authdata.success = Convert.ToBoolean(data["success"]);

            return authdata;
        }

        private async Task<object> ExecGetCommand(string command)
        {
            Log.WriteStart("ExecGetCommand {0}", command);

            AuthToken auth = await GetAccessToken();

            var commandurl = ServiceUrl + "/api/v1/" + command;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + auth.accessToken);
            var commandresponse = await client.GetAsync(commandurl);
            var commandresult = await commandresponse.Content.ReadAsStringAsync();
            Object commanddata = JsonConvert.DeserializeObject<dynamic>(commandresult);

            Log.WriteInfo("ExecGetCommand: URL: {0} \n\n returned: {1}", commandurl, commanddata);

            return commanddata;
        }

        private async Task<object> ExecPostCommand(string command, object param)
        {
            Log.WriteStart("ExecPostCommand {0}", command);

            AuthToken authToken = await GetAccessToken();

            var commandurl = ServiceUrl + "/api/v1/" + command;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            HttpClient client = new HttpClient();
            var commandparamjson = JsonConvert.SerializeObject(param);
            var commandinput_post = new StringContent(commandparamjson, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + authToken.accessToken);
            var commandresponse = await client.PostAsync(commandurl, commandinput_post);
            var commandresult = await commandresponse.Content.ReadAsStringAsync();
            Object commanddata = JsonConvert.DeserializeObject<object>(commandresult);

            Log.WriteInfo("ExecPostCommand: URL: {0} \n\n returned: {1}", commandurl, commanddata);

            return commanddata;
        }

        private async Task<object> ExecDomainGetCommand(string command, string domain)
        {
            AuthToken auth = await GetDomainAccessToken(domain);

            var commandurl = ServiceUrl + "/api/v1/" + command;

            Log.WriteError("ExecDomainGetCommand ", new Exception(auth.accessToken + " " + commandurl));

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + auth.accessToken);
            var commandresponse = await client.GetAsync(commandurl);
            var commandresult = await commandresponse.Content.ReadAsStringAsync();
            Object commanddata = JsonConvert.DeserializeObject<dynamic>(commandresult);

            //Log.WriteError("ExecDomainGetCommand ", new Exception(commandresult));

            Log.WriteInfo("ExecDomainGetCommand: URL: {0} \n\n returned: {1}", commandurl, commanddata);

            return commanddata;
        }

        private async Task<object> ExecDomainPostCommand(string command, string domain, object param)
        {
            AuthToken authToken = await GetDomainAccessToken(domain);

            var commandurl = ServiceUrl + "/api/v1/" + command;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            HttpClient client = new HttpClient();
            var commandparamjson = JsonConvert.SerializeObject(param);
            var commandinput_post = new StringContent(commandparamjson, Encoding.UTF8, "application/json");

            //Log.WriteError("ExecDomainPostCommand ", new Exception(commandparamjson + " " + commandurl));

            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + authToken.accessToken);
            var commandresponse = await client.PostAsync(commandurl, commandinput_post);
            var commandresult = await commandresponse.Content.ReadAsStringAsync();
            Object commanddata = JsonConvert.DeserializeObject<object>(commandresult);

            Log.WriteInfo("ExecDomainPostCommand: URL: {0} \n\n returned: {1}", commandurl, commanddata);

            return commanddata;
        }

        private async Task<object> ExecUserGetCommand(string command, string email)
        {
            Log.WriteStart("ExecGetCommand {0}", command);

            AuthToken auth = await GetUserAccessToken(email);

            var commandurl = ServiceUrl + "/api/v1/" + command;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + auth.accessToken);
            var commandresponse = await client.GetAsync(commandurl);
            var commandresult = await commandresponse.Content.ReadAsStringAsync();
            Object commanddata = JsonConvert.DeserializeObject<dynamic>(commandresult);

            Log.WriteInfo("ExecUserGetCommand: URL: {0} \n\n returned: {1}", commandurl, commanddata);

            return commanddata;
        }

        private async Task<object> ExecUserPostCommand(string command, string email, object param)
        {
            AuthToken authToken = await GetUserAccessToken(email);

            var commandurl = ServiceUrl + "/api/v1/" + command;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            HttpClient client = new HttpClient();
            var commandparamjson = JsonConvert.SerializeObject(param);
            var commandinput_post = new StringContent(commandparamjson, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + authToken.accessToken);
            var commandresponse = await client.PostAsync(commandurl, commandinput_post);
            var commandresult = await commandresponse.Content.ReadAsStringAsync();
            Object commanddata = JsonConvert.DeserializeObject<object>(commandresult);

            Log.WriteInfo("ExecUserPostCommand: URL: {0} \n\n returned: {1}", commandurl, commanddata);

            return commanddata;
        }

        #endregion

        #region Domains

        /// <summary>
        /// Checks whether the specified domain exists
        /// </summary>
        /// <param name="domainName">Domain name</param>
        /// <returns>true if the specified domain exists, otherwise false</returns>
        public bool DomainExists(string domainName)
        {
            try
            {
                dynamic result = ExecGetCommand("settings/sysadmin/domain-settings/" + domainName).Result;

                if (Convert.ToBoolean(result["success"]))
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                throw new Exception("Could not check whether mail domain exists", ex);
            }
        }

        public string[] GetDomains()
        {
            try
            {

                List<string> domainNames = new List<string>();

                dynamic result = ExecGetCommand("settings/sysadmin/domains").Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                foreach (dynamic domain in result["data"])
                {
                    string domainName = domain["name"].ToString();
                    domainNames.Add(domainName);
                }

                String[] domainNameString = domainNames.ToArray();

                return domainNameString;
            }
            catch (Exception ex)
            {
                throw new Exception("Could not get the list of mail domains", ex);
            }
        }

        public MailDomain GetDomain(string domainName)
        {
            try
            {
                dynamic result = ExecGetCommand("settings/sysadmin/domain-settings/" + domainName).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                Log.WriteInfo("GetDomain: DomainSettings {0}", result["domainSettings"]);

                // fill domain properties
                MailDomain domain = new MailDomain();
                domain.Name = domainName;
                domain.Path = result["domainSettings"]["domainPath"].ToString();

                //CurrentSize

                dynamic resultDomain = ExecGetCommand("settings/sysadmin/domain/" + domainName).Result;

                bool successDomain = Convert.ToBoolean(result["success"]);

                if (!successDomain)
                    throw new Exception(result["message"]);

                domain.SizeMB = (int)Convert.ToInt64(resultDomain["domainData"]["sizeMb"].ToString());

                //Features
                //domain.ShowContentFilteringMenu
                domain.ShowDomainAliasMenu = Convert.ToBoolean(result["domainSettings"]["showDomainAliasMenu"]);
                domain.ShowListMenu = Convert.ToBoolean(result["domainSettings"]["showListMenu"]);
                domain.ShowSpamMenu = Convert.ToBoolean(result["domainSettings"]["showSpamMenu"]);
                //Domain Reports
                //Enable POP Retrieval
                //Enable Catch - Alls
                //Enable IMAP Retreival
                //Enable Mail Signing
                //Enable Email Reports
                //Enable SyncML


                //Sharing
                domain.IsGlobalAddressList = Convert.ToBoolean(result["domainSettings"]["sharedGlobalAddressList"]);
                domain.SharedCalendars = Convert.ToBoolean(result["domainSettings"]["calendarPublicAvailability"]);
                //domain.SharedContacts 
                //domain.SharedFolders
                //domain.SharedNotes
                //domain.SharedTasks

                //Throttling
                domain[MailDomain.SMARTERMAIL5_MESSAGES_PER_HOUR] = result["domainSettings"]["throttleSettings"]["messagesPerHour"].ToString();
                string SM_MESSAGES_PER_HOUR_ENABLED = result["domainSettings"]["throttleSettings"]["messagesAction"].ToString();
                domain[MailDomain.SMARTERMAIL5_MESSAGES_PER_HOUR_ENABLED] = GetBoolean(SM_MESSAGES_PER_HOUR_ENABLED);
                domain[MailDomain.SMARTERMAIL5_BANDWIDTH_PER_HOUR] = result["domainSettings"]["throttleSettings"]["bandwidthPerHour"].ToString();
                string SM_BANDWIDTH_PER_HOUR_ENABLED = result["domainSettings"]["throttleSettings"]["bandwidthAction"].ToString();
                domain[MailDomain.SMARTERMAIL5_BANDWIDTH_PER_HOUR_ENABLED] = GetBoolean(SM_BANDWIDTH_PER_HOUR_ENABLED);
                domain[MailDomain.SMARTERMAIL5_BOUNCES_PER_HOUR] = result["domainSettings"]["throttleSettings"]["bouncesPerHour"].ToString();
                string SM_BOUNCES_PER_HOUR_ENABLED = result["domainSettings"]["throttleSettings"]["bouncesAction"].ToString();
                domain[MailDomain.SMARTERMAIL5_BOUNCES_PER_HOUR_ENABLED] = GetBoolean(SM_BOUNCES_PER_HOUR_ENABLED);

                //Limits
                domain.MaxDomainSizeInMB = (int)Convert.ToInt64(Convert.ToInt64(result["domainSettings"]["maxSize"].ToString()) / 1048576);
                domain.MaxDomainAliases = (int)Convert.ToInt64(result["domainSettings"]["maxDomainAliases"].ToString());
                domain.MaxDomainUsers = (int)Convert.ToInt64(result["domainSettings"]["maxUsers"].ToString());
                domain.MaxAliases = (int)Convert.ToInt64(result["domainSettings"]["maxAliases"].ToString());
                domain.MaxLists = (int)Convert.ToInt64(result["domainSettings"]["maxLists"].ToString());
                domain.MaxMessageSize = (int)Convert.ToInt64(result["domainSettings"]["maxMessageSize"].ToString());
                domain.MaxRecipients = (int)Convert.ToInt64(result["domainSettings"]["maxRecipients"].ToString());


                domain.MaxMailboxSizeInMB = (int)Convert.ToInt64(Convert.ToInt64(result["domainSettings"]["maxMailboxSize"].ToString()) / 1048576);
                domain.MaxRecipients = (int)Convert.ToInt64(result["domainSettings"]["maxRecipients"].ToString());
                domain.RequireSmtpAuthentication = Convert.ToBoolean(result["domainSettings"]["requireSmtpAuthentication"]);
                domain.ListCommandAddress = result["domainSettings"]["listCommandAddress"].ToString();

                // get additional domain settings
                domain.CatchAllAccount = result["domainSettings"]["catchAll"].ToString();
                domain.Enabled = Convert.ToBoolean(result["domainSettings"]["isEnabled"]);
                domain.BypassForwardBlackList = Convert.ToBoolean(result["domainSettings"]["bypassForwardBlackList"]);
                //domain.ServerIP = result["ServerIP;
                //domain.ImapPort = result["ImapPort;
                //domain.SmtpPort = result["SmtpPort;
                //domain.PopPort = result["PopPort;
                //domain.ShowContentFilteringMenu = result["ShowContentFilteringMenu;

                //TODO: Above options


                // get catch-all address
                if (!String.IsNullOrEmpty(domain.CatchAllAccount))
                {
                    // get catch-all group
                    string groupName = SYSTEM_CATCH_ALL + "@" + domain.Name;
                    if (GroupExists(groupName))
                    {
                        // get the first member of this group
                        MailGroup group = GetGroup(groupName);

                        if (group.Members.Length > 0)
                        {
                            domain.CatchAllAccount = GetAccountName(group.Members[0]);
                        }
                    }
                }

                dynamic licenseType = ExecGetCommand("licensing/info").Result;

                if (licenseType["edition"].ToString() == "0") //Enterprise
                {
                    domain[MailDomain.SMARTERMAIL_LICENSE_TYPE] = "ENT";
                }
                if (licenseType["edition"].ToString() == "1")  //Professional
                {
                    domain[MailDomain.SMARTERMAIL_LICENSE_TYPE] = "PRO";
                }
                if (licenseType["edition"].ToString() == "2") //Free
                {
                    domain[MailDomain.SMARTERMAIL_LICENSE_TYPE] = "FREE";
                }
                if (licenseType["edition"].ToString() == "3") //Free
                {
                    domain[MailDomain.SMARTERMAIL_LICENSE_TYPE] = "Lite";
                }

                return domain;
            }
            catch (Exception ex)
            {
                throw new Exception("Could not get mail domain", ex);
            }
        }

        public void CreateDomain(MailDomain domain)
        {
            try
            {
                var domainDataArray = new
                {
                    name = domain.Name,
                    path = DomainsPath + "\\" + domain.Name,
                    hostname = domain.Name,
                    isEnabled = domain.Enabled.ToString(),
                    userLimit = domain.MaxDomainUsers,
                    domainAliasCount = domain.MaxDomainAliases,
                    listLimit = domain.MaxLists,
                    size = domain.MaxDomainSizeInMB,
                    maxSize = domain.MaxDomainSizeInMB,
                    sizeMb = domain.MaxDomainSizeInMB,
                };

                Log.WriteError(new Exception(domain.MaxDomainSizeInMB.ToString()));

                var domainparam = new
                {
                    domainData = domainDataArray,
                    adminUsername = "system.domain.admin",
                    adminPassword = Guid.NewGuid().ToString("P")
                };

                dynamic result = ExecPostCommand("settings/sysadmin/domain-put", domainparam).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);
            }
            catch (Exception ex)
            {
                if (DomainExists(domain.Name))
                {
                    DeleteDomain(domain.Name);
                }
                Log.WriteError(ex);
                throw new Exception("Could not create mail domain", ex);
            }
        }

        public void DeleteDomain(string domainName)
        {
            try
            {
                var input_post = new { };
                dynamic result = ExecPostCommand("settings/sysadmin/domain-delete/" + domainName + "/true", input_post).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);
            }
            catch (Exception ex)
            {
                throw new Exception("Could not delete mail domain", ex);
            }
        }

        public bool DomainAliasExists(string domainName, string aliasName)
        {
            try
            {
                string[] aliases = GetDomainAliases(domainName);
                foreach (string alias in aliases)
                {
                    if (String.Compare(alias, aliasName, true) == 0)
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                throw new Exception("Could not check whether mail domain alias exists", ex);
            }
        }

        public string[] GetDomainAliases(string domainName)
        {
            try
            {
                List<string> domainAliasNames = new List<string>();

                dynamic result = ExecDomainGetCommand("settings/domain/domain-aliases", domainName).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                if (result["domainAliasData"] != null)
                {
                    foreach (dynamic domain in result["domainAliasData"])
                    {
                        string domainAliasName = domain["name"].ToString();
                        domainAliasNames.Add(domainAliasName);
                    }
                }

                return domainAliasNames.ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception("Could not get the list of mail domain aliases", ex);
            }
        }

        public void AddDomainAlias(string domainName, string aliasName)
        {
            try
            {
                var addDomainPram = new
                {
                };

                dynamic result = ExecDomainPostCommand("settings/domain/domain-alias-put/" + aliasName + "/false", domainName, addDomainPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);
            }
            catch (Exception ex)
            {
                throw new Exception("Could not add mail domain alias", ex);
            }
        }

        public void DeleteDomainAlias(string domainName, string aliasName)
        {
            try
            {

                var DelDomainPram = new
                {
                };

                dynamic result = ExecDomainPostCommand("settings/domain/domain-alias-delete/" + aliasName, domainName, DelDomainPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);
            }
            catch (Exception ex)
            {
                throw new Exception("Could not delete mail domain alias", ex);
            }
        }

        public void UpdateDomain(MailDomain domain)
        {
            try
            {

                var throttleSettingsArray = new
                {
                    bandwidthAction = domain[MailDomain.SMARTERMAIL5_BANDWIDTH_PER_HOUR_ENABLED],
                    bandwidthPerHour = domain[MailDomain.SMARTERMAIL5_BANDWIDTH_PER_HOUR],
                    bouncesAction = domain[MailDomain.SMARTERMAIL5_BANDWIDTH_PER_HOUR_ENABLED],
                    bouncesPerHour = domain[MailDomain.SMARTERMAIL5_BOUNCES_PER_HOUR]
                };

                var domainSettingsArray = new
                {
                    catchAll = domain.CatchAllAccount,
                    showDomainAliasMenu = domain.ShowDomainAliasMenu,
                    showListMenu = domain.ShowListMenu,
                    showSpamMenu = domain.ShowSpamMenu,
                    sharedGlobalAddressList = domain.IsGlobalAddressList,
                    calendarPublicAvailability = domain.SharedCalendars,
                    maxMessages = domain[MailDomain.SMARTERMAIL5_MESSAGES_PER_HOUR],
                    throttleSettings = throttleSettingsArray,
                    maxSize = Convert.ToInt64(domain.MaxDomainSizeInMB) * 1048576,
                    maxDomainAliases = domain.MaxDomainAliases,
                    maxUsers = domain.MaxDomainUsers,
                    maxMessageSize = domain.MaxMessageSize,
                    maxRecipients = domain.MaxRecipients,
                    maxLists = domain.MaxLists
                };

                var domainSettingsPram = new
                {
                    domainSettings = domainSettingsArray
                };

                dynamic result = ExecPostCommand("settings/sysadmin/domain-settings/" + domain.Name, domainSettingsPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

            }
            catch (Exception ex)
            {
                throw new Exception("Could not update mail domain", ex);
            }
        }

        #endregion

        #region Mail Accounts

        public bool AccountExists(string mailboxName)
        {
            var sysadminGetUserParams = new
            {
                email = mailboxName
            };

            dynamic result = ExecPostCommand("settings/sysadmin/get-user", sysadminGetUserParams).Result;

            if (Convert.ToBoolean(result["success"]))
                return true;

            return false;
        }

        public MailAccount[] GetAccounts(string domainName)
        {
            try
            {
                dynamic result = ExecGetCommand("settings/sysadmin/list-users/" + domainName).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                List<MailAccount> accounts = new List<MailAccount>();

                foreach (dynamic user in result["userData"])
                {
                    if (Convert.ToBoolean(user["securityFlags"]["isDomainAdmin"]) && !ImportDomainAdmin)
                        continue;

                    Log.WriteInfo("GetAccounts - Account: {0} \n\n DomainAdmin: {1} \n\n ImportDomainAdmin: {2}", user["emailAddress"].ToString(), Convert.ToBoolean(user["securityFlags"]["isDomainAdmin"]), ImportDomainAdmin);

                    MailAccount account = new MailAccount();
                    account.Name = user["emailAddress"].ToString();
                    account.Size = long.Parse(user["currentMailboxSize"].ToString());
                    //account.Password = user.password;
                    accounts.Add(account);
                }
                return accounts.ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception("Could not get the list of domain mailboxes", ex);
            }
        }

        public void CreateAccount(MailAccount mailbox)
        {
            try
            {
                var userDataArray = new
                {
                    domain = GetDomainName(mailbox.Name),
                    userName = mailbox.Name,
                    fullName = mailbox.FullName,
                    password = mailbox.Password,
                    maxMailboxSize = Convert.ToInt64(mailbox.MaxMailboxSize) * 1048576
                };

                var userContactInfoArray = new
                {
                    firstName = mailbox.FirstName,
                    lastName = mailbox.LastName,
                    displayAs = mailbox.FirstName + " " + mailbox.LastName
                };

                var userMailSettingsArray = new
                {
                    userContactInfo = userContactInfoArray,
                    replyToAddress = mailbox.ReplyTo,
                    signature = mailbox.Signature,
                    isEnabled = mailbox.Enabled,
                    enableMailForwarding = mailbox.ForwardingEnabled
                };

                var forwardListArray = new
                {
                    forwardList = mailbox.ForwardingAddresses,
                    deleteOnForward = mailbox.DeleteOnForward
                };

                var userputPram = new
                {
                    userData = userDataArray,
                    userMailSettings = userMailSettingsArray,
                    forwardList = forwardListArray
                };

                dynamic result = ExecDomainPostCommand("settings/domain/user-put", GetDomainName(mailbox.Name), userputPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                var autoResponderSettingsArray = new
                {
                    enabled = mailbox.ResponderEnabled,
                    subject = mailbox.ResponderSubject,
                    body = mailbox.ResponderMessage,
                    externalReply = mailbox.ResponderMessage
                };

                var autoresponderPramArray = new
                {
                    autoResponderSettings = autoResponderSettingsArray
                };

                result = ExecUserPostCommand("settings/auto-responder", mailbox.Name, autoresponderPramArray).Result;

                success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                //TODO: Signature

                if (mailbox.Signature != null)
                {
                    var signatureConfigArray = new
                    {
                        name = GetAccountName(mailbox.Name) + "Sig001",
                        text = mailbox.Signature,
                        isDefault = true
                    };

                    var signaturePram = new
                    {
                        signatureConfig = signatureConfigArray
                    };

                    dynamic signatureresult = ExecUserPostCommand("settings/user-signature-put", mailbox.Name, signaturePram).Result;

                    bool signaturesuccess = Convert.ToBoolean(signatureresult["success"]);
                    if (!signaturesuccess)
                        throw new Exception(signatureresult["message"]);

                    var signatureMapsArray = new[]
                    {
                        new {
                            allowUsersToOverride = true,
                            key = mailbox.Name,
                            mapOption = "2",
                            signatureGuid = signatureresult["signatureGuid"].ToString(),
                            type = "4"
                        }
                    };

                    var signatureMapsPram = new
                    {
                        signatureMaps = signatureMapsArray
                    };

                    dynamic signatureMapsresult = ExecUserPostCommand("settings/signature-mappings", mailbox.Name, signatureMapsPram).Result;

                    bool signatureMapssuccess = Convert.ToBoolean(signatureMapsresult["success"]);
                    if (!signatureMapssuccess)
                        throw new Exception(signatureMapsresult["message"]);
                }

            }
            catch (Exception ex)
            {
                if (AccountExists(mailbox.Name))
                {
                    DeleteAccount(mailbox.Name);
                }
                Log.WriteError(ex);
                throw new Exception("Could not create mailbox", ex);
            }
        }

        public MailAccount GetAccount(string mailboxName)
        {
            try
            {
                var sysadminGetUserParams = new
                {
                    email = mailboxName
                };

                dynamic result = ExecPostCommand("settings/sysadmin/get-user", sysadminGetUserParams).Result;

                bool userDatasuccess = Convert.ToBoolean(result["success"]);
                if (!userDatasuccess)
                    throw new Exception(result["message"]);

                MailAccount mailbox = new MailAccount();

                mailbox.Name = mailboxName;

                dynamic userMailSettingsresult = ExecUserGetCommand("settings/user-mail", mailboxName).Result;
                bool userMailSettingssuccess = Convert.ToBoolean(userMailSettingsresult["success"]);
                if (!userMailSettingssuccess)
                    throw new Exception(userMailSettingsresult["message"]);

                mailbox.FirstName = userMailSettingsresult["userMailSettings"]["userContactInfo"]["firstName"].ToString();
                mailbox.LastName = userMailSettingsresult["userMailSettings"]["userContactInfo"]["lastName"].ToString();
                mailbox.IsDomainAdmin = Convert.ToBoolean(result["userData"]["IsDomainAdmin"]);
                mailbox.Enabled = !Convert.ToBoolean(result["userData"]["securityFlags"]["isDisabled"].ToString());
                //string maxMailboxSizeint = userDataresult["userData"]["maxMailboxSize"].ToString();
                mailbox.MaxMailboxSize = (int)Convert.ToInt64(Convert.ToInt64(result["userData"]["maxMailboxSize"]) / 1048576);
                mailbox.PasswordLocked = Convert.ToBoolean(result["userData"]["passwordLocked"]);
                mailbox.ReplyTo = userMailSettingsresult["userMailSettings"]["replyToAddress"].ToString();
                mailbox.ForwardingEnabled = Convert.ToBoolean(userMailSettingsresult["userMailSettings"]["enableMailForwarding"]);

                dynamic userSignaturesresult = ExecUserGetCommand("settings/emails-signatures", mailboxName).Result;

                bool userSignaturessuccess = Convert.ToBoolean(userSignaturesresult["success"]);
                if (!userSignaturessuccess)
                    throw new Exception(userSignaturesresult["message"]);

                foreach (dynamic userSignature in userSignaturesresult["userSignatures"])
                {
                    if (Convert.ToBoolean(userSignature["isDefault"]))
                    {
                        mailbox.Signature = userSignature["text"].ToString();
                        mailbox.SignatureGuid = userSignature["guid"].ToString();
                        mailbox.SignatureName = userSignature["name"].ToString();
                        mailbox.SignatureiD = (int)Convert.ToInt64(userSignature["id"]);
                    }
                }

                dynamic mailboxForwardListresult = ExecUserGetCommand("settings/mailbox-forward-list", mailboxName).Result;

                bool mailboxForwardListsuccess = Convert.ToBoolean(mailboxForwardListresult["success"]);
                if (!mailboxForwardListsuccess)
                    throw new Exception(mailboxForwardListresult["message"]);

                List<string> ForwardingAddress = new List<string>();

                List<string> forwardListArray = mailboxForwardListresult["mailboxForwardList"]["forwardList"].ToObject<List<string>>();

                foreach (dynamic address in forwardListArray)
                {
                    ForwardingAddress.Add(address);
                }

                string[] ForwardingAddressString = ForwardingAddress.ToArray();
                mailbox.ForwardingAddresses = ForwardingAddressString;
                mailbox.DeleteOnForward = Convert.ToBoolean(mailboxForwardListresult["mailboxForwardList"]["deleteOnForward"]);

                dynamic autoResponderSettingsresult = ExecUserGetCommand("settings/auto-responder", mailboxName).Result;

                bool autoResponderSettingssuccess = Convert.ToBoolean(autoResponderSettingsresult["success"]);
                if (!autoResponderSettingssuccess)
                    throw new Exception(autoResponderSettingsresult["message"]);

                mailbox.ResponderEnabled = Convert.ToBoolean(autoResponderSettingsresult["autoResponderSettings"]["enabled"]);

                mailbox.ResponderSubject = autoResponderSettingsresult["autoResponderSettings"]["subject"].ToString();

                mailbox.ResponderMessage = autoResponderSettingsresult["autoResponderSettings"]["body"].ToString();

                return mailbox;
            }
            catch (Exception ex)
            {
                throw new Exception("Could not get mailbox", ex);
            }
        }

        public void DeleteAccount(string mailboxName)
        {
            try
            {
                var input_post = new { };
                dynamic result = ExecDomainPostCommand("settings/domain/user-delete/" + GetAccountName(mailboxName), GetDomainName(mailboxName), input_post).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);
            }
            catch (Exception ex)
            {
                throw new Exception("Could not delete mailbox", ex);
            }
        }

        public void UpdateAccount(MailAccount mailbox)
        {
            try
            {
                //get original account
                MailAccount account = GetAccount(mailbox.Name);

                if (mailbox.ChangePassword)
                {
                    var passworduserDataArray = new
                    {
                        password = mailbox.Password
                    };

                    var passwordPram = new
                    {
                        email = mailbox.Name,
                        userData = passworduserDataArray
                    };

                    dynamic passwordresult = ExecDomainPostCommand("settings/domain/post-user", GetDomainName(mailbox.Name), passwordPram).Result;

                    bool passwordsuccess = Convert.ToBoolean(passwordresult["success"]);
                    if (!passwordsuccess)
                        throw new Exception(passwordresult["message"]);
                }

                var userContactInfoArray = new
                {
                    firstName = mailbox.FirstName,
                    lastName = mailbox.LastName,
                    displayAs = mailbox.FirstName + " " + mailbox.LastName
                };
                var userMailSettingsArray = new
                {
                    userContactInfo = userContactInfoArray,
                    isEnabled = mailbox.Enabled,
                    enableMailForwarding = mailbox.ForwardingEnabled,
                    replyToAddress = mailbox.ReplyTo
                    //signature = mailbox.Signature,
                };

                var userputPram = new
                {
                    email = mailbox.Name,
                    userMailSettings = userMailSettingsArray
                };

                dynamic result = ExecDomainPostCommand("settings/domain/post-user-mail", GetDomainName(mailbox.Name), userputPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                var updateUseruserDataArray = new
                {
                    fullName = mailbox.FirstName + " " + mailbox.LastName,
                    maxMailboxSize = Convert.ToInt64(mailbox.MaxMailboxSize) * 1048576
                };

                var updateUserPram = new
                {
                    email = mailbox.Name,
                    userData = updateUseruserDataArray,
                };

                dynamic updateUserresult = ExecDomainPostCommand("settings/domain/post-user", GetDomainName(mailbox.Name), updateUserPram).Result;

                bool updateUsersuccess = Convert.ToBoolean(updateUserresult["success"]);
                if (!updateUsersuccess)
                    throw new Exception(updateUserresult["message"]);

                var forwardListArray = new
                {
                    forwardList = mailbox.ForwardingAddresses,
                    deleteOnForward = mailbox.DeleteOnForward
                };

                var forwardPut = new
                {
                    mailboxForwardList = forwardListArray
                };

                dynamic forwardresult = ExecUserPostCommand("settings/mailbox-forward-list", mailbox.Name, forwardPut).Result;

                bool forwardsuccess = Convert.ToBoolean(forwardresult["success"]);
                if (!forwardsuccess)
                    throw new Exception(forwardresult["message"]);

                var autoResponderSettingsArray = new
                {
                    enabled = mailbox.ResponderEnabled,
                    subject = mailbox.ResponderSubject,
                    body = mailbox.ResponderMessage,
                    externalReply = mailbox.ResponderMessage
                };

                var autoResponderPram = new
                {
                    autoResponderSettings = autoResponderSettingsArray
                };

                dynamic autoResponderresult = ExecUserPostCommand("settings/auto-responder", mailbox.Name, autoResponderPram).Result;

                bool autoRespondersuccess = Convert.ToBoolean(autoResponderresult["success"]);
                if (!autoRespondersuccess)
                    throw new Exception(autoResponderresult["message"]);

                //// TODO: Signature
                //Log.WriteInfo("Sig: {0}", mailbox.Signature);

                if (mailbox.Signature != null)
                {
                    //Check if creating a new Signature or updating one
                    if (account.SignatureGuid == null)
                    {
                        string signatureName = account.SignatureName ?? GetAccountName(mailbox.Name) + "Sig001";
                        var signatureConfigArray = new
                        {
                            name = signatureName,
                            text = mailbox.Signature,
                            isDefault = true,
                        };

                        var signaturePram = new
                        {
                            signatureConfig = signatureConfigArray
                        };

                        dynamic signatureresult = ExecUserPostCommand("settings/user-signature-put", mailbox.Name, signaturePram).Result;

                        bool signaturesuccess = Convert.ToBoolean(signatureresult["success"]);
                        if (!signaturesuccess)
                            throw new Exception(signatureresult["message"]);

                        var signatureMapsArray = new[]
                            {
                            new {
                                allowUsersToOverride = true,
                                key = mailbox.Name,
                                mapOption = "2",
                                signatureGuid = signatureresult["signatureGuid"],
                                type = "4"
                            }
                        };

                        var signatureMapsPram = new
                        {
                            signatureMaps = signatureMapsArray
                        };

                        dynamic signatureMapsresult = ExecUserPostCommand("settings/signature-mappings", mailbox.Name, signatureMapsPram).Result;

                        bool signatureMapssuccess = Convert.ToBoolean(signatureMapsresult["success"]);
                        if (!signatureMapssuccess)
                            throw new Exception(signatureMapsresult["message"]);
                    }
                    else
                    {
                        //Updating signature
                        var signatureConfigArray = new
                        {
                            id = account.SignatureiD,
                            guid = account.SignatureGuid,
                            name = account.SignatureName,
                            text = mailbox.Signature,
                            isDefault = true
                        };

                        var signaturePram = new
                        {
                            signatureConfig = signatureConfigArray
                        };

                        dynamic signatureresult = ExecUserPostCommand("settings/user-signature", mailbox.Name, signaturePram).Result;

                        bool signaturesuccess = Convert.ToBoolean(signatureresult["success"]);
                        if (!signaturesuccess)
                            throw new Exception(signatureresult["message"]);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Could not update mailbox", ex);
            }
        }

        #endregion

        #region Mail Aliases

        public bool MailAliasExists(string mailAliasName)
        {
            try
            {
                dynamic result = ExecDomainGetCommand("settings/domain/aliases/" + GetAccountName(mailAliasName), GetDomainName(mailAliasName)).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                Log.WriteInfo("MailAliasExists - A gridInfo: {0}", result);
                if (result["gridInfo"].ToString() != "")
                {
                    foreach (dynamic aliases in result["gridInfo"])
                    {
                        Log.WriteInfo("MailAliasExists - B aliases:\n {0}", aliases["name"].ToString());
                        if (aliases["name"].ToString() == mailAliasName)
                        {
                            Log.WriteInfo("MailAliasExists - Found alias: {0}", mailAliasName);
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                throw new Exception("Could not check whether mail alias exists", ex);
            }
        }

        public MailAlias[] GetMailAliases(string domainName)
        {
            try
            {
                dynamic result = ExecDomainGetCommand("settings/domain/aliases/", domainName).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                List<MailAlias> aliasesList = new List<MailAlias>();

                if (result["gridInfo"] != null)
                {
                    foreach (dynamic alias in result["gridInfo"])
                    {
                        MailAlias mailAlias = new MailAlias();
                        mailAlias.Name = alias["name"].ToString() + "@" + domainName;

                        List<string> members = new List<string>();

                        if (alias["targets"] != null)
                        {
                            foreach (string member in alias["targets"])
                            {
                                members.Add(member);
                            }
                        }

                        if (members.ToArray().Length == 1)
                        {
                            Log.WriteInfo("GetMailAliases - Found {0}", alias["name"].ToString());
                            mailAlias.ForwardTo = alias["targets"][0].ToString();
                            aliasesList.Add(mailAlias);
                        }
                    }
                }

                return aliasesList.ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception("Could not get the list of mail aliases", ex);
            }


        }

        public void CreateMailAlias(MailAlias mailAlias)
        {
            try
            {
                var aliasArray = new
                {
                    name = GetAccountName(mailAlias.Name),
                    aliasTargetList = new string[] { mailAlias.ForwardTo }
                };

                var mailAliasPram = new
                {
                    alias = aliasArray
                };


                dynamic result = ExecDomainPostCommand("settings/domain/alias-put", GetDomainName(mailAlias.Name), mailAliasPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

            }

            catch (Exception ex)
            {
                if (MailAliasExists(mailAlias.Name))
                {
                    DeleteMailAlias(mailAlias.Name);
                }
                Log.WriteError(ex);
                throw new Exception("Could not create mail alias", ex);

            }

        }

        public MailAlias GetMailAlias(string mailAliasName)
        {
            MailAlias alias = new MailAlias();

            dynamic result = ExecDomainGetCommand("settings/domain/alias/" + GetAccountName(mailAliasName), GetDomainName(mailAliasName)).Result;

            bool success = Convert.ToBoolean(result["success"]);
            if (!success)
                throw new Exception(result["message"]);

            alias.Name = result["alias"]["name"].ToString();

            if (result["alias"]["aliasTargetList"] != null)
            {
                alias.ForwardTo = result["alias"]["aliasTargetList"][0].ToString();
            }
            else
            {
                alias.ForwardTo = "";
            }
            return alias;
        }

        public void DeleteMailAlias(string mailAliasName)
        {
            try
            {

                var mailAliasPram = new
                {
                };

                dynamic result = ExecDomainPostCommand("settings/domain/alias-delete/" + GetAccountName(mailAliasName), GetDomainName(mailAliasName), mailAliasPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

            }
            catch (Exception ex)
            {
                throw new Exception("Could not delete mailAlias", ex);
            }
        }

        public void UpdateMailAlias(MailAlias mailAlias)
        {
            try
            {
                dynamic resultGetAlias = ExecDomainGetCommand("settings/domain/alias/" + mailAlias.Name, GetDomainName(mailAlias.Name)).Result;

                bool successAlias = Convert.ToBoolean(resultGetAlias["success"]);
                if (!successAlias)
                    throw new Exception(resultGetAlias["message"]);

                var aliasArray = new
                {
                    name = GetAccountName(mailAlias.Name),
                    aliasTargetList = new string[] { mailAlias.ForwardTo },
                    enableForXmpp = Convert.ToBoolean(resultGetAlias["alias"]["enableForXmpp"])
                };

                var mailAliasPram = new
                {
                    alias = aliasArray
                };

                dynamic result = ExecDomainPostCommand("settings/domain/alias", GetDomainName(mailAlias.Name), mailAliasPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

            }
            catch (Exception ex)
            {
                throw new Exception("Could not update mailAlias", ex);
            }

        }

        #endregion

        #region Groups

        public bool GroupExists(string groupName)
        {
            try
            {
                dynamic result = ExecDomainGetCommand("settings/domain/aliases/" + GetAccountName(groupName), GetDomainName(groupName)).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                dynamic gridInfo = result["gridInfo"];

                if (gridInfo != null)
                {
                    foreach (var item in gridInfo)
                    {
                        if (item["name"].ToString() == groupName?.Split('@')[0])
                        {
                            Log.WriteInfo("GroupExists - Found group: {0}", groupName);
                            return true;
                        }
                    }
                }

                Log.WriteInfo("GroupExists - Could not find group: {0}", groupName);

                return false;
            }
            catch (Exception ex)
            {
                throw new Exception("Could not check whether mail domain group exists", ex);
            }
        }

        public MailGroup[] GetGroups(string domainName)
        {
            try
            {
                dynamic result = ExecDomainGetCommand("settings/domain/aliases/", domainName).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                List<MailGroup> groups = new List<MailGroup>();

                if (result["gridInfo"] != null)
                {
                    foreach (dynamic alias in result["gridInfo"])
                    {
                        MailGroup mailGroup = new MailGroup();
                        mailGroup.Name = alias["name"] + "@" + domainName;

                        List<string> members = new List<string>();

                        if (alias["targets"] != null)
                        {
                            foreach (string member in alias["targets"])

                            {
                                members.Add(member);
                            }
                        }

                        if (members.ToArray().Length > 1)
                        {
                            mailGroup.Members = members.ToArray();
                            groups.Add(mailGroup);
                        }
                    }
                }

                return groups.ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception("Could not get the list of mail domain groups", ex);
            }
        }

        public MailGroup GetGroup(string groupName)
        {
            try
            {
                dynamic result = ExecDomainGetCommand("settings/domain/alias/" + GetAccountName(groupName), GetDomainName(groupName)).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);


                List<string> targets = new List<string>();

                if (result["alias"]["aliasTargetList"] != null)
                {
                    foreach (string target in result["alias"]["aliasTargetList"])
                    {
                        string targetName = target;
                        targets.Add(targetName);
                    };
                }

                String[] targetsString = targets.ToArray();

                MailGroup group = new MailGroup();
                group.Name = groupName;
                group.Members = targetsString;
                group.Enabled = true; // by default
                return group;
            }
            catch (Exception ex)
            {
                throw new Exception("Could not get mail domain group", ex);
            }
        }

        public void CreateGroup(MailGroup group)
        {
            try
            {
                var aliasArray = new
                {
                    name = GetAccountName(group.Name),
                    aliasTargetList = group.Members
                };

                var mailAliasPram = new
                {
                    alias = aliasArray
                };

                dynamic result = ExecDomainPostCommand("settings/domain/alias-put", GetDomainName(group.Name), mailAliasPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);
            }
            catch (Exception ex)
            {
                throw new Exception("Could not create mail domain group", ex);
            }
        }

        public void DeleteGroup(string groupName)
        {
            try
            {

                var mailAliasPram = new
                {
                };

                dynamic result = ExecDomainPostCommand("settings/domain/alias-delete/" + GetAccountName(groupName), GetDomainName(groupName), mailAliasPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);
            }
            catch (Exception ex)
            {
                throw new Exception("Could not delete mail domain group", ex);
            }
        }

        public void UpdateGroup(MailGroup group)
        {
            try
            {
                dynamic resultGetAlias = ExecDomainGetCommand("settings/domain/alias/" + GetAccountName(group.Name), GetDomainName(group.Name)).Result;

                bool successAlias = Convert.ToBoolean(resultGetAlias["success"]);
                if (!successAlias)
                    throw new Exception(resultGetAlias["message"]);

                var aliasArray = new
                {
                    name = GetAccountName(group.Name),
                    aliasTargetList = group.Members,
                    enableForXmpp = Convert.ToBoolean(resultGetAlias["alias"]["enableForXmpp"])
                };

                var mailAliasPram = new
                {
                    alias = aliasArray
                };

                dynamic result = ExecDomainPostCommand("settings/domain/alias", GetDomainName(group.Name), mailAliasPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);
            }
            catch (Exception ex)
            {
                throw new Exception("Could not update mail domain group", ex);
            }
        }

        #endregion

        #region Lists

        public bool ListExists(string listName)
        {
            try
            {
                dynamic result = ExecDomainGetCommand("settings/domain/mailing-lists/list", GetDomainName(listName)).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                foreach (dynamic member in result["items"])
                {
                    string MemberlistAddress = member.listAddress;
                    if (MemberlistAddress.ToLower() == GetAccountName(listName).ToLower())
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Couldn't obtain mail list.", ex);
            }

            return false;
        }

        public void CreateList(MailList list)
        {
            try
            {
                int enumPostingPermissions = 2;
                if (list.PostingMode == PostingMode.AnyoneCanPost)
                {
                    enumPostingPermissions = 0;
                }
                else if (list.PostingMode == PostingMode.MembersCanPost)
                {
                    enumPostingPermissions = 1;
                }

                var AliasPram = new
                {
                    listAddress = GetAccountName(list.Name),
                    moderatorAddress = list.ModeratorAddress,
                    description = list.Description,
                    password = list.Password,
                    requirePassword = list.RequirePassword,
                    postingPermissions = enumPostingPermissions,
                    prependSubject = list.SubjectPrefix,
                    //list.EnableSubjectPrefix
                    //list.MaxMessageSize
                    //list.MaxRecipientsPerMessage
                    //list.ListReplyToAddress
                    listToAddress = list.ListToAddress,
                    listFromAddress = list.ListFromAddress,
                    listReplyToAddress = list.ListReplyToAddress,
                    enableDigest = list.DigestMode,
                    sendSubscribeEmail = list.SendSubscribe,
                    sendUnsubscribeEmail = list.SendUnsubscribe,
                    //Enable Unsubscribe from Subject: ?? list.AllowUnsubscribe
                    //disableListCommand = list.DisableListcommand
                    // list.DisableSubscribecommand
                };

                dynamic result = ExecDomainPostCommand("settings/domain/mailing-lists/add", GetDomainName(list.Name), AliasPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                //Add members

                if (list.Members.Length > 0)
                {
                    dynamic memberaddresult = ExecDomainPostCommand("settings/domain/mailing-lists/" + result["item.id"] + "/subscriber-add", GetDomainName(list.Name), list.Members).Result;

                    bool memberaddsuccess = Convert.ToBoolean(memberaddresult["success"]);
                    if (!memberaddsuccess)
                        throw new Exception(memberaddresult["message"]);
                }
            }
            catch (Exception ex)
            {
                if (ListExists(list.Name))
                {
                    DeleteList(list.Name);
                }
                Log.WriteError(ex);
                throw new Exception("Couldn't create mail list.", ex);
            }
        }

        public MailList GetList(string listName)
        {
            try
            {

                MailList list = new MailList();
                list.Name = listName;
                List<string> members = new List<string>();

                dynamic result = ExecDomainGetCommand("settings/domain/mailing-lists/list", GetDomainName(listName)).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                foreach (dynamic member in result["items"])
                {
                    string MemberlistAddress = member["listAddress"].ToString();
                    if ((MemberlistAddress.ToLower()) == (GetAccountName(listName).ToLower()))
                    {
                        list.Id = (int)Convert.ToInt64(member["id"]);
                        list.CreatedDate = Convert.ToDateTime(member["createdOn"]);
                        list.Description = member["description"].ToString();
                        //list.Enabled
                        list.ModeratorAddress = member["moderatorAddress"].ToString();
                        //list.Moderated
                        list.Password = member["password"].ToString();
                        list.RequirePassword = Convert.ToBoolean(member["requirePassword"]);

                        var enumPostingPermissions = PostingMode.ModeratorCanPost;

                        if (member["postingPermissions"].ToString() == "0")
                        {
                            enumPostingPermissions = PostingMode.AnyoneCanPost;
                        }
                        else if (member["postingPermissions"].ToString() == "1")
                        {
                            enumPostingPermissions = PostingMode.MembersCanPost;
                        }

                        list.PostingMode = enumPostingPermissions;
                        list.SubjectPrefix = member["subject"].ToString();
                        list.EnableSubjectPrefix = Convert.ToBoolean(member["prependSubject"]);
                        //list.MaxMessageSize
                        //list.MaxRecipientsPerMessage
                        //list.ReplyToMode
                        list.ListToAddress = member["listAddress"].ToString();
                        list.ListFromAddress = member["listFromAddress"].ToString();
                        list.ListReplyToAddress = member["listReplyToAddress"].ToString();

                        // list members
                        if ((int)Convert.ToInt64(member["listSubscriberCount"]) > 0)
                        {
                            var listSubscriberArray = new
                            {
                                skip = 0,
                                take = (int)Convert.ToInt64(member["listSubscriberCount"]),
                                search = "",
                                subscriberType = "Subscriber",
                                sortField = "emailaddress"
                            };

                            dynamic listSubscriberresult = ExecDomainPostCommand("settings/domain/mailing-lists/" + member.id + "/subscriber-search", GetDomainName(listName), listSubscriberArray).Result;

                            bool listSubscribersuccess = Convert.ToBoolean(listSubscriberresult["success"]);
                            if (!listSubscribersuccess)
                                throw new Exception(listSubscriberresult["message"]);

                            foreach (dynamic item in listSubscriberresult["items"])
                            {
                                string itememail = item["emailAddress"].ToString();
                                members.Add(itememail);
                            }
                        }

                        list.Members = members.ToArray();
                        list.DigestMode = Convert.ToBoolean(member["enableDigest"]);
                        list.SendSubscribe = Convert.ToBoolean(member["sendSubscribeEmail"]);
                        list.SendUnsubscribe = Convert.ToBoolean(member["sendUnsubscribeEmail"]);
                        // list.AllowUnsubscribe = Enable Unsubscribe from Subject
                        //list.DisableListcommand = Convert.ToBoolean(member["disableListcommand"]);
                        //list.DisableSubscribecommand = member.disableSubscribecommand;
                    }
                }

                return list;
            }
            catch (Exception ex)
            {
                throw new Exception("Couldn't obtain mail list.", ex);
            }
        }

        public MailList[] GetLists(string domainName)
        {
            try
            {
                dynamic result = ExecDomainGetCommand("settings/domain/mailing-lists/list", domainName).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                List<MailList> mailLists = new List<MailList>();

                foreach (dynamic member in result["items"])
                {
                    List<string> members = new List<string>();
                    MailList list = new MailList();
                    list.Name = member["listAddress"].ToString() + "@" + domainName;
                    list.Id = (int)Convert.ToInt64(member["id"]);
                    list.CreatedDate = Convert.ToDateTime(member["createdOn"]);
                    list.Description = member["description"].ToString();
                    //list.Enabled
                    list.ModeratorAddress = member["moderatorAddress"].ToString();
                    //list.Moderated
                    list.Password = member["password"].ToString();
                    list.RequirePassword = Convert.ToBoolean(member.requirePassword);

                    var enumPostingPermissions = PostingMode.ModeratorCanPost;

                    if (member["postingPermissions"].ToString() == "0")
                    {
                        enumPostingPermissions = PostingMode.AnyoneCanPost;
                    }
                    else if (member["postingPermissions"].ToString() == "1")
                    {
                        enumPostingPermissions = PostingMode.MembersCanPost;
                    }

                    list.PostingMode = enumPostingPermissions;

                    list.SubjectPrefix = member["subject"].ToString();
                    list.EnableSubjectPrefix = Convert.ToBoolean(member["prependSubject"]);
                    //list.MaxMessageSize
                    //list.MaxRecipientsPerMessage
                    //list.ReplyToMode
                    list.ListToAddress = member["listAddress"].ToString();
                    list.ListFromAddress = member["listFromAddress"].ToString();
                    list.ListReplyToAddress = member["listReplyToAddress"].ToString();

                    // list members
                    if (member.listSubscriberCount > 0)
                    {
                        var listSubscriberArray = new
                        {
                            skip = 0,
                            take = 200,
                            search = "",
                            subscriberType = "Subscriber",
                            sortField = "emailaddress"
                        };

                        dynamic listSubscriberresult = ExecDomainPostCommand("settings/domain/mailing-lists/" + member.id + "/subscriber-search", domainName, listSubscriberArray).Result;

                        bool listSubscribersuccess = Convert.ToBoolean(listSubscriberresult["success"]);
                        if (!listSubscribersuccess)
                            throw new Exception(listSubscriberresult["message"]);

                        foreach (dynamic item in listSubscriberresult["items"])
                        {
                            string itememail = item["emailAddress"].ToString();
                            members.Add(itememail);
                        }
                    }

                    list.Members = members.ToArray();
                    list.DigestMode = Convert.ToBoolean(member["enableDigest"]);
                    list.SendSubscribe = Convert.ToBoolean(member["sendSubscribeEmail"]);
                    list.SendUnsubscribe = Convert.ToBoolean(member["sendUnsubscribeEmail"]);
                    // list.AllowUnsubscribe = Enable Unsubscribe from Subject
                    //list.DisableListcommand = Convert.ToBoolean(member["disableListcommand"]);
                    //list.DisableSubscribecommand = member.disableSubscribecommand;
                    mailLists.Add(list);
                }

                return mailLists.ToArray();

            }
            catch (Exception ex)
            {
                throw new Exception("Couldn't obtain domain mail lists.", ex);
            }
        }

        public void DeleteList(string listName)
        {
            try
            {
                dynamic result = ExecDomainGetCommand("settings/domain/mailing-lists/list", GetDomainName(listName)).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                foreach (dynamic member in result["items"])
                {
                    string MemberlistAddress = member.listAddress;
                    if (MemberlistAddress.ToLower() == listName.ToLower())
                    {
                        var DelListPram = new
                        {
                        };

                        dynamic deleteresult = ExecDomainPostCommand("settings/domain/mailing-lists/" + member.id + "/delete", GetDomainName(listName), DelListPram).Result;

                        bool deletesuccess = Convert.ToBoolean(deleteresult["success"]);
                        if (!success)
                            throw new Exception(deleteresult["message"]);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Couldn't delete a mail list.", ex);
            }
        }

        private void SetMailListSettings(MailList list, string[] smSettings)
        {
            foreach (string setting in smSettings)
            {
                string[] bunch = setting.Split(new char[] { '=' });

                switch (bunch[0])
                {
                    case "description":
                        list.Description = bunch[1];
                        break;
                    case "disabled":
                        list.Enabled = !Convert.ToBoolean(bunch[1]);
                        break;
                    case "moderator":
                        list.ModeratorAddress = bunch[1];
                        list.Moderated = !string.IsNullOrEmpty(bunch[1]);
                        break;
                    case "password":
                        list.Password = bunch[1];
                        break;
                    case "requirepassword":
                        list.RequirePassword = Convert.ToBoolean(bunch[1]);
                        break;
                    case "whocanpost":
                        if (string.Compare(bunch[1], "anyone", true) == 0)
                            list.PostingMode = PostingMode.AnyoneCanPost;
                        else if (string.Compare(bunch[1], "moderator", true) == 0)
                            list.PostingMode = PostingMode.ModeratorCanPost;
                        else
                            list.PostingMode = PostingMode.MembersCanPost;
                        break;
                    case "prependsubject":
                        list.EnableSubjectPrefix = Convert.ToBoolean(bunch[1]);
                        break;
                    case "maxmessagesize":
                        list.MaxMessageSize = (int)Convert.ToInt64(bunch[1]);
                        break;
                    case "maxrecipients":
                        list.MaxRecipientsPerMessage = (int)Convert.ToInt64(bunch[1]);
                        break;
                    case "replytolist":
                        list.ReplyToMode = string.Compare(bunch[1], "true", true) == 0 ? ReplyTo.RepliesToList : ReplyTo.RepliesToSender;
                        break;
                    case "subject":
                        list.SubjectPrefix = bunch[1];
                        break;
                    case "listtoaddress":
                        if (string.Compare(bunch[1], "DEFAULT", true) == 0)
                            list.ListToAddress = "DEFAULT";
                        else if (string.Compare(bunch[1], "LISTADDRESS", true) == 0)
                            list.ListToAddress = "LISTADDRESS";
                        else if (string.Compare(bunch[1], "SUBSCRIBERADDRESS", true) == 0)
                            list.ListToAddress = "SUBSCRIBERADDRESS";
                        else
                            list.ListToAddress = bunch[1];
                        break;
                    case "listfromaddress":
                        if (string.Compare(bunch[1], "LISTADDRESS", true) == 0)
                            list.ListFromAddress = "LISTADDRESS";
                        else list.ListFromAddress = string.Compare(bunch[1], "POSTERADDRESS", true) == 0 ? "POSTERADDRESS" : bunch[1];
                        break;
                    case "listreplytoaddress":
                        if (string.Compare(bunch[1], "LISTADDRESS", true) == 0)
                            list.ListReplyToAddress = "LISTADDRESS";
                        else list.ListReplyToAddress = string.Compare(bunch[1], "POSTERADDRESS", true) == 0 ? "POSTERADDRESS" : bunch[1];
                        break;
                    case "digestmode":
                        list.DigestMode = Convert.ToBoolean(bunch[1]);
                        break;
                    case "sendsubscribe":
                        list.SendSubscribe = Convert.ToBoolean(bunch[1]);
                        break;
                    case "sendunsubscribe":
                        list.SendUnsubscribe = Convert.ToBoolean(bunch[1]);
                        break;
                    case "allowunsubscribe":
                        list.AllowUnsubscribe = Convert.ToBoolean(bunch[1]);
                        break;
                    case "disablelistcommand":
                        list.DisableListcommand = Convert.ToBoolean(bunch[1]);
                        break;
                    case "disablesubscribecommand":
                        list.DisableSubscribecommand = Convert.ToBoolean(bunch[1]);
                        break;

                }
            }
        }

        protected void SetMailListMembers(MailList list, string[] subscribers)
        {
            List<string> members = new List<string>();

            foreach (string subscriber in subscribers)
                members.Add(subscriber);

            list.Members = members.ToArray();
        }

        public void UpdateList(MailList list)
        {
            try
            {
                string domain = GetDomainName(list.Name);
                string listAccount = GetAccountName(list.Name);

                dynamic Listsresult = ExecDomainGetCommand("settings/domain/mailing-lists/list", domain).Result;

                bool Listssuccess = Convert.ToBoolean(Listsresult["success"]);
                if (!Listssuccess)
                    throw new Exception(Listsresult["message"]);

                string listID = "";
                int subscriberCount = 0;

                foreach (dynamic member in Listsresult["items"])
                {
                    string MemberlistAddress = member.listAddress;
                    if (MemberlistAddress.ToLower() == listAccount.ToLower())
                    {
                        listID = member.id;
                        subscriberCount = (int)Convert.ToInt64(member.listSubscriberCount);
                    }
                }

                int enumPostingPermissions = 2;
                if (list.PostingMode == PostingMode.AnyoneCanPost)
                {
                    enumPostingPermissions = 0;
                }
                else if (list.PostingMode == PostingMode.MembersCanPost)
                {
                    enumPostingPermissions = 1;
                }

                var mailListPram = new
                {
                    description = list.Description,
                    listAddress = listAccount,
                    moderatorAddress = list.ModeratorAddress,
                    postingPermissions = enumPostingPermissions,
                    //disableListCommand = list.DisableListcommand,
                    //disableSubscriptions
                    enableDigest = list.DigestMode,
                    prependSubject = list.SubjectPrefix,
                    //subject
                    requirePassword = list.RequirePassword,
                    password = list.Password,
                    //maxMessagesSentPerHour: 
                    //messagesAction: enum (see ThrottlingActions),
                    //maxSmtpOutBandwidthPerHour: 
                    //bandwidthAction: enum (see ThrottlingActions),
                    disabled = !list.Enabled,
                    sendSubscribeEmail = list.SendSubscribe,
                    sendUnsubscribeEmail = list.SendUnsubscribe,
                    //digestMailbox: 
                    //digestSubject: 
                    //digestStripNonTextAttachments: 
                    //digestSendTriggerSize: 
                    //digestSendTriggerType: enum (see DigestTrigger),
                    //digestSendType: enum (see DigestSendType),
                    //digestNextSendDate: date,
                    //doubleOptIn: 
                    //disableListErrorReplies: 
                    listToAddress = list.ListToAddress,
                    listFromAddress = list.ListFromAddress,
                    listReplyToAddress = list.ListReplyToAddress
                    //customUnsubscribe: 
                    //unsubscribeText: 
                    //baseUrl: 
                    //showInGal: boolean
                };

                dynamic result = ExecDomainPostCommand("settings/domain/mailing-lists/" + listID + "/settings", domain, mailListPram).Result;

                bool success = Convert.ToBoolean(result["success"]);
                if (!success)
                    throw new Exception(result["message"]);

                var listSubscriberArray = new
                {
                    skip = 0,
                    take = subscriberCount,
                    search = "",
                    subscriberType = "Subscriber",
                    sortField = "emailaddress"
                };

                dynamic listSubscriberresult = ExecDomainPostCommand("settings/domain/mailing-lists/" + listID + "/subscriber-search", domain, listSubscriberArray).Result;

                bool listSubscribersuccess = Convert.ToBoolean(listSubscriberresult["success"]);
                if (!listSubscribersuccess)
                    throw new Exception(listSubscriberresult["message"]);

                var listEmailRemove = new List<string>();

                foreach (dynamic item in listSubscriberresult["items"])
                {
                    listEmailRemove.Add((string)item.emailAddress);
                }

                dynamic mailRemoveMemberresult = ExecDomainPostCommand("settings/domain/mailing-lists/" + listID + "/subscriber-remove", domain, listEmailRemove).Result;

                bool mailRemoveMembersuccess = Convert.ToBoolean(mailRemoveMemberresult["success"]);
                if (!success)
                    throw new Exception(mailRemoveMemberresult["message"]);

                if (list.Members.Length > 0)
                {
                    dynamic SetSubscriberListresult = ExecDomainPostCommand("settings/domain/mailing-lists/" + listID + "/subscriber-add", domain, list.Members).Result;

                    bool SetSubscriberListsuccess = Convert.ToBoolean(SetSubscriberListresult["success"]);
                    if (!success)
                        throw new Exception(SetSubscriberListresult["message"]);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Couldn't update mail list.", ex);
            }
        }

        #endregion

        #region Install Settings
        public override bool IsInstalled()
        {
            string productName = null;
            string productVersion = null;

            RegistryKey HKLM = Registry.LocalMachine;

            RegistryKey key = HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            String[] names = null;

            if (key != null)
            {
                names = key.GetSubKeyNames();

                foreach (string s in names)
                {
                    RegistryKey subkey = HKLM.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + s);
                    if (subkey != null)
                        if (!String.IsNullOrEmpty((string)subkey.GetValue("DisplayName")))
                        {
                            productName = (string)subkey.GetValue("DisplayName");
                        }
                    if (productName != null && productName.Contains("SmarterMail"))
                    {
                        if (subkey != null)
                            productVersion = (string)subkey.GetValue("DisplayVersion");
                        break;
                    }
                }

                if (!String.IsNullOrEmpty(productVersion))
                {
                    int version = 0;
                    string[] split = productVersion.Split(new[] { '.' });

                    if (int.TryParse(split[0], out version))
                    {
                        if (version >= 100) return true;
                    }
                    else
                        return split[0].Equals("100");
                }
            }

            //checking x64 platform
            key = HKLM.OpenSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall");

            if (key == null)
            {
                return false;
            }

            names = key.GetSubKeyNames();

            foreach (string s in names)
            {
                RegistryKey subkey = HKLM.OpenSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + s);
                if (subkey != null)
                    if (!String.IsNullOrEmpty((string)subkey.GetValue("DisplayName")))
                    {
                        productName = (string)subkey.GetValue("DisplayName");
                    }
                if (productName != null)
                    if (productName.Contains("SmarterMail"))
                    {
                        if (subkey != null) productVersion = (string)subkey.GetValue("DisplayVersion");
                        break;
                    }
            }

            if (!String.IsNullOrEmpty(productVersion))
            {
                int version = 0;
                string[] split = productVersion.Split(new[] { '.' });

                if (int.TryParse(split[0], out version))
                {
                    if (version >= 100) return true;
                }
                else
                    return split[0].Equals("100");
            }

            return false;
        }
        #endregion

        protected string GetDomainName(string email)
        {
            return email.Substring(email.IndexOf('@') + 1);
        }

        protected string GetAccountName(string email)
        {
            return email.Substring(0, email.IndexOf('@'));
        }

        protected string GetBoolean(string Boolean)
        {
            if (Boolean == "1" | Boolean == "true")
            {
                return "true";
            }
            else
            {
                return "false";
            }
        }
    }
}
