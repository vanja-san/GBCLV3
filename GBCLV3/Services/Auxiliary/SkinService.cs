﻿using GBCLV3.Models.Auxiliary;
using GBCLV3.Services.Launch;
using StyletIoC;
using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace GBCLV3.Services.Auxiliary
{
    class SkinService
    {
        #region Private Fields

        private readonly GamePathService _gamePathService;

        private const string DEFAULT_PROFILE_SERVER = "https://sessionserver.mojang.com/session/minecraft/profile/";

        private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly JsonSerializerOptions _jsonOptions
            = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        #endregion

        #region Constructor

        [Inject]
        public SkinService(GamePathService gamePathService)
        {
            _gamePathService = gamePathService;
        }

        #endregion

        public async ValueTask<string> GetProfileAsync(string uuid, string profileServer = null)
        {
            try
            {
                string profileJson = await _client.GetStringAsync(profileServer ?? DEFAULT_PROFILE_SERVER + uuid);
                using var profile = JsonDocument.Parse(profileJson);

                return profile.RootElement
                              .GetProperty("properties")[0]
                              .GetProperty("value")
                              .GetString();
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(ex.ToString());
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ERROR] Index json download time out");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return null;
        }

        public async ValueTask<Skin> GetSkinAsync(string profile)
        {
            try
            {
                string skinJson = Encoding.UTF8.GetString(Convert.FromBase64String(profile));
                using var skinDoc = JsonDocument.Parse(skinJson);
                var textures = skinDoc.RootElement.GetProperty("textures");

                var skin = new Skin();

                if (textures.TryGetProperty("SKIN", out var body))
                {
                    skin.IsSlim = body.TryGetProperty("metadata", out _);
                    string url = body.GetProperty("url").GetString();
                    skin.Body = await LoadSkin(url);
                }

                if (textures.TryGetProperty("CAPE", out var cape))
                {
                    string url = body.GetProperty("url").GetString();
                    skin.Body = await LoadSkin(url);
                }

                skin.Face = GetFace(skin.Body);
                return skin;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                return null;
            }
        }

        #region Private Methods

        private async ValueTask<BitmapImage> LoadSkin(string url)
        {
            int pos = url.LastIndexOf('/') + 1;
            string hash = url[pos..];
            string path = $"{_gamePathService.AssetsDir}/{hash[..2]}/{hash}";

            if (!File.Exists(path))
            {
                await DownloadAsync(url, path);
            }

            return LoadFromDisk(path);
        }

        private static BitmapImage LoadFromDisk(string path)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();

            return img;
        }

        private static async Task DownloadAsync(string url, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using var httpStream = await _client.GetStreamAsync(url);
            using var fileStream = File.OpenWrite(path);
            await httpStream.CopyToAsync(fileStream);
            fileStream.Flush();
        }

        private static CroppedBitmap GetFace(BitmapImage body)
        {
            int regionSize = body.PixelWidth / 8;
            return new CroppedBitmap(body, new Int32Rect(regionSize, regionSize, regionSize, regionSize));
        }

        #endregion
    }
}
