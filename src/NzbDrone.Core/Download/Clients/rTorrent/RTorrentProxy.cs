﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Extensions;
using CookComputing.XmlRpc;

namespace NzbDrone.Core.Download.Clients.RTorrent
{
    public interface IRTorrentProxy
    {
        string GetVersion(RTorrentSettings settings);
        List<RTorrentTorrent> GetTorrents(RTorrentSettings settings);

        void AddTorrentFromUrl(string torrentUrl, RTorrentSettings settings);
        void AddTorrentFromFile(string fileName, byte[] fileContent, RTorrentSettings settings);
        void RemoveTorrent(string hash, RTorrentSettings settings);
        void SetTorrentPriority(string hash, RTorrentPriority priority, RTorrentSettings settings);
        void SetTorrentLabel(string hash, string label, RTorrentSettings settings);
        void SetTorrentDownloadDirectory(string hash, string directory, RTorrentSettings settings);
        bool HasHashTorrent(string hash, RTorrentSettings settings);
        void StartTorrent(string hash, RTorrentSettings settings);
        void AddTorrentMultiCall(string hash, string label, RTorrentSettings settings);
    }

    public interface IRTorrent : IXmlRpcProxy
    {
        [XmlRpcMethod("d.multicall2")]
        object[] TorrentMulticall(params string[] parameters);

        [XmlRpcMethod("load.start")]
        int LoadUrl(string target, string data);

        [XmlRpcMethod("load.raw")]
        int LoadBinary(string target, byte[] data);

        [XmlRpcMethod("d.erase")]
        int Remove(string hash);

        [XmlRpcMethod("d.custom1.set")]
        string SetLabel(string hash, string label);

        [XmlRpcMethod("d.priority.set")]
        int SetPriority(string hash, long priority);

        [XmlRpcMethod("d.directory.set")]
        int SetDirectory(string hash, string directory);

        [XmlRpcMethod("d.name")]
        string GetName(string hash);

        [XmlRpcMethod("system.client_version")]
        string GetVersion();

        [XmlRpcMethod("system.multicall")]
        object[] SystemMulticall(object[] parameters);

    }

    public class RTorrentProxy : IRTorrentProxy
    {
        private readonly Logger _logger;

        public RTorrentProxy(Logger logger)
        {
            _logger = logger;
        }

        public string GetVersion(RTorrentSettings settings)
        {
            _logger.Debug("Executing remote method: system.client_version");

            var client = BuildClient(settings);

            var version = client.GetVersion();

            return version;
        }

        public List<RTorrentTorrent> GetTorrents(RTorrentSettings settings)
        {
            _logger.Debug("Executing remote method: d.multicall2");

            var client = BuildClient(settings);
            var ret = client.TorrentMulticall("", "",
                "d.name=", // string
                "d.hash=", // string
                "d.base_path=", // string
                "d.custom1=", // string (label)
                "d.size_bytes=", // long
                "d.left_bytes=", // long
                "d.down.rate=", // long (in bytes / s)
                "d.ratio=", // long
                "d.is_open=", // long
                "d.is_active=", // long
                "d.complete="); //long

            var items = new List<RTorrentTorrent>();
            foreach (object[] torrent in ret)
            {
                var labelDecoded = System.Web.HttpUtility.UrlDecode((string)torrent[3]);

                var item = new RTorrentTorrent();
                item.Name = (string)torrent[0];
                item.Hash = (string)torrent[1];
                item.Path = (string)torrent[2];
                item.Category = labelDecoded;
                item.TotalSize = (long)torrent[4];
                item.RemainingSize = (long)torrent[5];
                item.DownRate = (long)torrent[6];
                item.Ratio = (long)torrent[7];
                item.IsOpen = Convert.ToBoolean((long)torrent[8]);
                item.IsActive = Convert.ToBoolean((long)torrent[9]);
                item.IsFinished = Convert.ToBoolean((long)torrent[10]);

                items.Add(item);
            }

            return items;
        }

        public void AddTorrentFromUrl(string torrentUrl, RTorrentSettings settings)
        {
            _logger.Debug("Executing remote method: load.normal");

            var client = BuildClient(settings);

            var response = client.LoadUrl("", torrentUrl);
            if (response != 0)
            {
                throw new DownloadClientException("Could not add torrent: {0}.", torrentUrl);
            }
        }

        public void AddTorrentFromFile(string fileName, byte[] fileContent, RTorrentSettings settings)
        {
            _logger.Debug("Executing remote method: load.raw");

            var client = BuildClient(settings);

            var response = client.LoadBinary("", fileContent);
            if (response != 0)
            {
                throw new DownloadClientException("Could not add torrent: {0}.", fileName);
            }
        }

        public void RemoveTorrent(string hash, RTorrentSettings settings)
        {
            _logger.Debug("Executing remote method: d.erase");

            var client = BuildClient(settings);

            var response = client.Remove(hash);
            if (response != 0)
            {
                throw new DownloadClientException("Could not remove torrent: {0}.", hash);
            }
        }

        public void SetTorrentPriority(string hash, RTorrentPriority priority, RTorrentSettings settings)
        {
            _logger.Debug("Executing remote method: d.priority.set");

            var client = BuildClient(settings);

            var response = client.SetPriority(hash, (long)priority);
            if (response != 0)
            {
                throw new DownloadClientException("Could not set priority on torrent: {0}.", hash);
            }
        }

        public void SetTorrentLabel(string hash, string label, RTorrentSettings settings)
        {
            _logger.Debug("Executing remote method: d.set_custom1");

            var labelEncoded = System.Web.HttpUtility.UrlEncode(label);

            var client = BuildClient(settings);

            var setLabel = client.SetLabel(hash, labelEncoded);
            if (setLabel != labelEncoded)
            {
                throw new DownloadClientException("Could set label on torrent: {0}.", hash);
            }
        }

        public void SetTorrentDownloadDirectory(string hash, string directory, RTorrentSettings settings)
        {
            _logger.Debug("Executing remote method: d.directory.set");

            var client = BuildClient(settings);

            var response = client.SetDirectory(hash, directory);
            if (response != 0)
            {
                throw new DownloadClientException("Could not set directory for torrent: {0}.", hash);
            }
        }

        public bool HasHashTorrent(string hash, RTorrentSettings settings)
        {
            _logger.Debug("Executing remote method: d.name");

            var client = BuildClient(settings);

            try
            {
                var name = client.GetName(hash);
                if (name.IsNullOrWhiteSpace()) return false;
                bool metaTorrent = name == (hash + ".meta");
                return !metaTorrent;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void StartTorrent(string hash, RTorrentSettings settings)
        {
            _logger.Debug("Executing remote methods: d.open and d.start");

            var client = BuildClient(settings);

            var multicallResponse = client.SystemMulticall(new[]
                                                  {
                                                      new
                                                      {
                                                          methodName = "d.open",
                                                          @params = new[] { hash }
                                                      },
                                                      new
                                                      {
                                                          methodName = "d.start",
                                                          @params = new[] { hash }
                                                      },
                                                  }).SelectMany(c => ((IEnumerable<int>)c));

            if (multicallResponse.Any(r => r != 0))
            {
                throw new DownloadClientException("Could not start torrent: {0}.", hash);
            }

        }

        public void AddTorrentMultiCall(string hash, string label, RTorrentSettings settings)
        {
            _logger.Debug("Executing remote methods: system.multicall");

            var labelEncoded = System.Web.HttpUtility.UrlEncode(label);

            var client = BuildClient(settings);

            var multicallResponse = client.SystemMulticall(new[]
                                                {
                                                      new
                                                      {
                                                          methodName = "session.path",
                                                          @params = new[] { "" }
                                                      },
                                                      new
                                                      {
                                                          methodName = "d.custom4",
                                                          @params = new[] { hash }
                                                      },
                                                      new
                                                      {
                                                          methodName = "d.tied_to_file",
                                                          @params = new[] { hash }
                                                      },
                                                      new
                                                      {
                                                          methodName = "d.custom1",
                                                          @params = new[] { hash, labelEncoded }
                                                      },
                                                      new
                                                      {
                                                          methodName = "d.directory_base",
                                                          @params = new[] { hash }
                                                      },
                                                      new
                                                      {
                                                          methodName = "d.is_private",
                                                          @params = new[] { hash }
                                                      },
                                                      new
                                                      {
                                                          methodName = "d.name",
                                                          @params = new[] { hash }
                                                      },
                                                  });

            var sessionPath = ((string[])multicallResponse[0])[0];
            var custom4 = ((string[])multicallResponse[1])[0];
            var tiedToFile = ((string[])multicallResponse[2])[0];
            var custom1 = ((string[])multicallResponse[3])[0];
            var directory = ((string[])multicallResponse[4])[0];
            var isPrivate = ((Int64[])multicallResponse[5])[0];
            var name = ((string[])multicallResponse[6])[0];

            _logger.Trace("session.path: " + sessionPath);
            _logger.Trace("custom4: " + custom4);
            _logger.Trace("tiedToFile: " + tiedToFile);
            _logger.Trace("custom1: " + custom1);
            _logger.Trace("directory: " + directory);
            _logger.Trace("isPrivate: " + isPrivate);
            _logger.Trace("name: " + name);

        }

        private IRTorrent BuildClient(RTorrentSettings settings)
        {
            var client = XmlRpcProxyGen.Create<IRTorrent>();

            client.Url = string.Format(@"{0}://{1}:{2}/{3}",
                                    settings.UseSsl ? "https" : "http",
                                    settings.Host,
                                    settings.Port,
                                    settings.UrlBase);

            client.EnableCompression = true;

            if (!settings.Username.IsNullOrWhiteSpace())
            {
                client.Credentials = new NetworkCredential(settings.Username, settings.Password);
            }

            return client;
        }
    }
}
