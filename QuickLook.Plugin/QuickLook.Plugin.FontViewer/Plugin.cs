﻿// Copyright © 2017-2025 QL-Win Contributors
//
// This file is part of QuickLook program.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using QuickLook.Plugin.HtmlViewer;
using QuickLook.Typography.OpenFont;
using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Resources;

namespace QuickLook.Plugin.FontViewer;

public class Plugin : IViewer
{
    private static readonly string _resourcePath = Path.Combine(SettingHelper.LocalDataPath, "QuickLook.Plugin.FontViewer");

    private WebpagePanel _panel;

    public int Priority => 0;

    public void Init()
    {
    }

    public bool CanHandle(string path)
    {
        // The `*.eot` and `*.svg` font types are not supported
        // TODO: Check `*.otc` type
        return !Directory.Exists(path) && new string[] { ".ttf", ".otf", ".woff", ".woff2", ".ttc" }.Any(path.ToLower().EndsWith);
    }

    public void Prepare(string path, ContextObject context)
    {
        context.PreferredSize = new Size { Width = 1300, Height = 650 };
    }

    public void View(string path, ContextObject context)
    {
        _panel = new WebpagePanel();

        if (OSThemeHelper.AppsUseDarkTheme())
        {
            // Invoke using reflection: WebView2.CreationProperties.AdditionalBrowserArguments
            // This approach allows the library to avoid direct dependency on WebView2
            if (typeof(WebpagePanel).GetField("_webView", BindingFlags.NonPublic | BindingFlags.Instance) is FieldInfo fieldInfo)
            {
                object webView2 = fieldInfo.GetValue(_panel);

                if (webView2?.GetType().GetProperty("CreationProperties", BindingFlags.Public | BindingFlags.Instance) is PropertyInfo creationPropertiesProperty)
                {
                    object creationProperties = creationPropertiesProperty.GetValue(webView2);

                    if (creationProperties?.GetType().GetProperty("AdditionalBrowserArguments", BindingFlags.Public | BindingFlags.Instance) is PropertyInfo additionalBrowserArgumentsProperty)
                    {
                        string additionalBrowserArguments = (additionalBrowserArgumentsProperty.GetValue(creationProperties) as string) ?? string.Empty;
                        additionalBrowserArgumentsProperty.SetValue(creationProperties, additionalBrowserArguments + " --enable-features=WebContentsForceDark");
                    }
                }
            }
        }

        context.ViewerContent = _panel;
        context.Title = Path.GetFileName(path);

        var html = GenerateFontHtml(path);
        var htmlPath = Path.Combine(_resourcePath, "font2html.html");

        if (!Directory.Exists(Path.GetDirectoryName(htmlPath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(htmlPath));
        }
        File.WriteAllText(htmlPath, html);

        _panel.FallbackPath = Path.GetDirectoryName(path);
        _panel.NavigateToFile(htmlPath);

        context.IsBusy = false;
    }

    private string GenerateFontHtml(string path)
    {
        string fontFamilyName = FreeTypeApi.GetFontFamilyName(path);
        StreamResourceInfo info = Application.GetResourceStream(new Uri($"pack://application:,,,/QuickLook.Plugin.FontViewer;component/Resources/font2html.html"));
        using Stream stream = info?.Stream;
        using StreamReader streamReader = new(stream, Encoding.UTF8);
        string html = streamReader.ReadToEnd();

        // src: url('xxx.eot');
        // src: url('xxx?#iefix') format('embedded-opentype'),
        //      url('xxx.woff') format('woff'),
        //      url('xxx.ttf') format('truetype'),
        //      url('xxx.svg#xxx') format('svg');
        var fileName = Path.GetFileName(path);
        var fileExt = Path.GetExtension(fileName);

        string cssUrl = $"src: url('{fileName}')"
            + fileExt switch
            {
                ".eot" => " format('embedded-opentype');",
                ".woff" => " format('woff');",
                ".woff2" => " format('woff2');",
                ".ttf" => " format('truetype');",
                ".otf" => " format('opentype');",
                _ => ";",
            };

        if (string.IsNullOrEmpty(fontFamilyName))
        {
            if (fileExt.ToLower().Equals(".woff2"))
            {
                fontFamilyName = Woff2.GetFontInfo(path)?.Name;
            }
        }

        // https://en.wikipedia.org/wiki/Pangram
        string translationFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Translations.config");
        string pangram = TranslationHelper.Get("SAMPLE_TEXT", translationFile);

        html = html.Replace("--font-family;", $"font-family: '{fontFamilyName}';")
                   .Replace("--font-url;", cssUrl)
                   .Replace("{{h1}}", fontFamilyName ?? fileName)
                   .Replace("{{pangram}}", pangram ?? "The quick brown fox jumps over the lazy dog. 0123456789");

        return html;
    }

    public void Cleanup()
    {
        GC.SuppressFinalize(this);
    }
}

file static class ResourcesProvider
{
    static ResourcesProvider()
    {
        if (!UriParser.IsKnownScheme("pack"))
        {
            _ = PackUriHelper.UriSchemePack;
        }
    }

    public static bool TryGetStream(Uri uri, out Stream stream)
    {
        try
        {
            StreamResourceInfo info = Application.GetResourceStream(uri);
            stream = info?.Stream;
            return true;
        }
        catch
        {
        }
        stream = null;
        return false;
    }
}
