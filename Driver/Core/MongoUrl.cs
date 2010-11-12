﻿/* Copyright 2010 10gen Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using MongoDB.Driver.Internal;

namespace MongoDB.Driver {
    [Serializable]
    public class MongoUrl {
        #region private fields
        private ConnectionMode connectionMode;
        private string databaseName;
        private string password;
        private string replicaSetName;
        private SafeMode safeMode;
        private IEnumerable<MongoServerAddress> servers;
        private bool slaveOk;
        private string username;
        #endregion

        #region constructors
        public MongoUrl() {
        }

        public MongoUrl(
            string connectionString
        ) {
            Parse(connectionString);
        }
        #endregion

        #region public properties
        public ConnectionMode ConnectionMode {
            get { return connectionMode; }
            set { connectionMode = value; }
        }

        public string DatabaseName {
            get { return databaseName; }
            set { databaseName = value; }
        }

        public string Password {
            get { return password; }
            set { password = value; }
        }

        public string ReplicaSetName {
            get { return replicaSetName; }
            set { replicaSetName = value; }
        }

        public SafeMode SafeMode {
            get { return safeMode; }
            set { safeMode = value; }
        }

        public MongoServerAddress Server {
            get { return (servers == null) ? null : servers.Single(); }
            set { servers = new MongoServerAddress[] { value }; }
        }

        public IEnumerable<MongoServerAddress> Servers {
            get { return servers; }
            set { servers = value; }
        }

        public bool SlaveOk {
            get { return slaveOk; }
            set { slaveOk = value; }
        }

        public string Username {
            get { return username; }
            set { username = value; }
        }
        #endregion

        #region public methods
        public void Parse(
            string connectionString
        ) {
            const string pattern =
                @"^mongodb://" +
                @"((?<username>[^:]+):(?<password>[^@]+)@)?" +
                @"(?<servers>[^:,/]+(:\d+)?(,[^:,/]+(:\d+)?)*)" +
                @"(/(?<database>[^?]+)?(\?(?<query>.*))?)?$";
            Match match = Regex.Match(connectionString, pattern);
            if (match.Success) {
                string username = match.Groups["username"].Value;
                string password = match.Groups["password"].Value;
                string servers = match.Groups["servers"].Value;
                string databaseName = match.Groups["database"].Value;
                string query = match.Groups["query"].Value;

                List<MongoServerAddress> addresses = new List<MongoServerAddress>();
                foreach (string server in servers.Split(',')) {
                    MongoServerAddress address;
                    if (MongoServerAddress.TryParse(server, out address)) {
                        addresses.Add(address);
                    } else {
                        throw new ArgumentException("Invalid connection string");
                    }
                }

                if (addresses.Count == 1) {
                    this.connectionMode = ConnectionMode.Direct;
                } else {
                    this.connectionMode = ConnectionMode.ReplicaSet;
                }

                if (!string.IsNullOrEmpty(query)) {
                    var setSafeMode = false;
                    var safe = false;
                    var w = 0;
                    var wtimeout = 0;
                    var fsync = false;

                    foreach (var pair in query.Split('&', ';')) {
                        var parts = pair.Split('=');
                        if (parts.Length != 2) {
                            throw new ArgumentException("Invalid connection string");
                        }
                        var name = parts[0];
                        var value = parts[1];

                        switch (name) {
                            case "connect":
                                switch (value) {
                                    case "direct":
                                        this.connectionMode = ConnectionMode.Direct;
                                        this.replicaSetName = null;
                                        break;
                                    case "replicaset":
                                        this.connectionMode = ConnectionMode.ReplicaSet;
                                        break;
                                    default:
                                        throw new ArgumentException("Invalid connection string");
                                }
                                break;
                            case "fsync":
                                setSafeMode = true;
                                switch (value) {
                                    case "false":
                                        fsync = false;
                                        break;
                                    case "true":
                                        fsync = true;
                                        break;
                                    default:
                                        throw new ArgumentException("Invalid connection string");
                                }
                                safe = true;
                                break;
                            case "replicaset":
                                this.replicaSetName = value;
                                this.connectionMode = ConnectionMode.ReplicaSet;
                                break;
                            case "safe":
                                setSafeMode = true;
                                switch (value) {
                                    case "false":
                                        safe = false;
                                        break;
                                    case "true":
                                        safe = true;
                                        break;
                                    default:
                                        throw new ArgumentException("Invalid connection string");
                                }
                                break;
                            case "slaveok":
                                switch (value) {
                                    case "false":
                                        this.slaveOk = false;
                                        break;
                                    case "true":
                                        this.slaveOk = true;
                                        break;
                                    default:
                                        throw new ArgumentException("Invalid connection string");
                                }
                                break;
                            case "w":
                                setSafeMode = true;
                                if (!int.TryParse(value, out w)) {
                                    throw new ArgumentException("Invalid connection string");
                                }
                                safe = true;
                                break;
                            case "wtimeout":
                                setSafeMode = true;
                                if (!int.TryParse(value, out wtimeout)) {
                                    throw new ArgumentException("Invalid connection string");
                                }
                                safe = true;
                                break;
                        }
                    }

                    if (setSafeMode) {
                        this.safeMode = SafeMode.Create(safe, fsync, w, TimeSpan.FromMilliseconds(wtimeout));
                    }
                }

                this.servers = addresses;
                this.databaseName = (databaseName != "") ? databaseName : null;
                this.username = (username != "") ? username : null;
                this.password = (password != "") ? password : null;
            } else {
                throw new ArgumentException("Invalid connection string");
            }
        }

        public MongoConnectionSettings ToConnectionSettings() {
            return new MongoConnectionSettings {
                Servers = Servers,
                DatabaseName = DatabaseName,
                Credentials = MongoCredentials.Create(Username, Password),
                ConnectionMode = ConnectionMode,
                ReplicaSetName = ReplicaSetName,
                SafeMode = SafeMode,
                SlaveOk = SlaveOk
            };
        }

        public override string ToString() {
            StringBuilder url = new StringBuilder();
            url.Append("mongodb://");
            if (username != null && password != null) {
                url.AppendFormat("{0}:{1}@", username, password);
            }
            bool firstServer = true;
            foreach (MongoServerAddress server in servers) {
                if (!firstServer) { url.Append(","); }
                if (server.Port == 27017) {
                    url.Append(server.Host);
                } else {
                    url.AppendFormat("{0}:{1}", server.Host, server.Port);
                }
                firstServer = false;
            }
            if (databaseName != null) {
                url.Append("/");
                url.Append(databaseName);
            }
            var query = new StringBuilder();
            if (
                connectionMode == ConnectionMode.Direct && servers.Count() != 1 ||
                connectionMode == Driver.ConnectionMode.ReplicaSet && servers.Count() == 1
            ) {
                query.AppendFormat("connect={0};", connectionMode.ToString().ToLower());
            }
            if (!string.IsNullOrEmpty(replicaSetName)) {
                query.AppendFormat("replicaset={0};", replicaSetName);
            }
            if (safeMode != null && safeMode.Enabled) {
                query.AppendFormat("safe=true;");
                if (safeMode.FSync) {
                    query.AppendFormat("fsync={0};", (safeMode.FSync) ? "true" : "false");
                }
                if (safeMode.W != 0) {
                    query.AppendFormat("w={0};", safeMode.W);
                    if (safeMode.WTimeout != TimeSpan.Zero) {
                        query.AppendFormat("wtimeout={0};", (int) safeMode.WTimeout.TotalMilliseconds);
                    }
                }
            }
            if (slaveOk) {
                query.AppendFormat("slaveok={0};", (slaveOk) ? "true" : "false");
            }
            if (query.Length != 0) {
                query.Length = query.Length - 1; // remove trailing ";"
                if (databaseName == null) {
                    url.Append("/");
                }
                url.Append("?");
                url.Append(query.ToString());
            }
            return url.ToString();
        }
        #endregion
    }
}
