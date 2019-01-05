﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Blazor;
using Microsoft.JSInterop;
using Toolbelt.Blazor.I18nText.Internals;

namespace Toolbelt.Blazor.I18nText
{
    public class I18nText
    {
        private readonly HttpClient HttpClient;

        private string FallbackLanguage = "en";

        private string _CurrentLanguage = "en";

        private readonly List<TextTable> TextTables = new List<TextTable>();

        public I18nText(IServiceProvider serviceProvider)
        {
            this.HttpClient = serviceProvider.GetService(typeof(HttpClient)) as HttpClient;
            var refThis = new DotNetObjectRef(this);
            JSRuntime.Current
                .InvokeAsync<string>("Toolbelt.Blazor.I18nText.initLang", refThis)
                .ContinueWith(_ => refThis.Dispose());
        }

        [JSInvokable(nameof(InitLang)), EditorBrowsable(EditorBrowsableState.Never)]
        public void InitLang(string[] langCodes)
        {
            _CurrentLanguage = langCodes.FirstOrDefault() ?? "en";
        }

        public string CurrentLanguage => _CurrentLanguage;

        public async Task SetCurrentLanguageAsync(string langCode)
        {
            if (this._CurrentLanguage == langCode) return;

            this._CurrentLanguage = langCode;
            var allRefreshTasks = this.TextTables.Select(tt => tt.RefreshTableAsync.Invoke(tt.Table));
            await Task.WhenAll(allRefreshTasks);
        }

        public async Task<T> GetTextTableAsync<T>() where T : class, new()
        {
            var fetchedTextTable = this.TextTables.FirstOrDefault(tt => tt.TableType == typeof(T));
            if (fetchedTextTable != null) return fetchedTextTable.Table as T;

            var table = await FetchTextTableAsync<T>();

            var textTable = new TextTable
            {
                TableType = typeof(T),
                Table = table,
                RefreshTableAsync = (t) => FetchTextTableAsync<T>().ContinueWith(task =>
                {
                    var result = task.Result;
                    var fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public);
                    foreach (var field in fields) field.SetValue(t, field.GetValue(result));
                })
            };
            this.TextTables.Add(textTable);
            return table;
        }

        private async Task<T> FetchTextTableAsync<T>() where T : class, new()
        {
            string[] splitLangCode(string lang)
            {
                var splitedLang = lang.Split('-');
                return splitedLang.Length == 1 ? new[] { lang } : new[] { lang, splitedLang[0] };
            }
            var langs = new List<string>(capacity: 4);
            langs.AddRange(splitLangCode(this._CurrentLanguage));
            langs.AddRange(splitLangCode(this.FallbackLanguage));

            var table = default(T);
            foreach (var lang in langs)
            {
                try
                {
                    var jsonUrl = "content/i18ntext/" + typeof(T).FullName + "." + lang + ".json";
                    table = await this.HttpClient.GetJsonAsync<T>(jsonUrl);
                    break;
                }
                catch (Exception) { }
            }

            if (table == null)
            {
                table = Activator.CreateInstance<T>();
                var fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public);
                foreach (var field in fields) field.SetValue(table, field.Name);
            }

            return table;
        }

    }
}