namespace PreloadAlert
{
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    internal class PreloadsContainer
    {
        private readonly Dictionary<string, int> indexes = new(1000);
        private readonly List<PreloadInfo> preloads = new(1000);
        private List<string> keysCache = new(1000);
        private bool isCacheValid = true;

        public void Remove(string key)
        {
            var cache = this.GetUpToDateCache();
            if (this.indexes.Remove(key, out var i))
            {
                this.preloads.RemoveAt(i);
                for (var j = i + 1; j < cache.Count; j++)
                {
                    this.indexes[cache[j]]--;
                }
            }

            this.isCacheValid = false;
        }

        public void Add(string key, PreloadInfo value, int index)
        {
            if (index >= this.preloads.Count)
            {
                this.preloads.Add(value);
                this.indexes.Add(key, this.preloads.Count - 1);
                return;
            }

            var cache = this.GetUpToDateCache();
            for (var j = index; j < cache.Count; j++)
            {
                this.indexes[cache[j]]++;
            }

            this.preloads.Insert(index, value);
            this.indexes.Add(key, index);
            this.isCacheValid = false;
        }

        public void AddOrUpdate(string key, PreloadInfo value, int index)
        {
            if (this.indexes.TryGetValue(key, out var i))
            {
                if (i == index)
                {
                    this.preloads[i] = value;
                    return;
                }

                // priority changed
                this.Remove(key);
            }

            this.Add(key, value, index);
        }

        public PreloadInfo Get(string key)
        {
            if (this.indexes.TryGetValue (key, out var i))
            {
                return this.preloads[i];
            }
            else
            {
                return new();
            }
        }

        public void Clear()
        {
            this.preloads.Clear();
            this.indexes.Clear();
            this.keysCache.Clear();
            this.isCacheValid = true;
        }

        public void Load(string filepathname)
        {
            if (File.Exists(filepathname))
            {
                var content = File.ReadAllText(filepathname);
                var preloadList = JsonConvert.DeserializeObject<List<KeyValuePair<string, PreloadInfo>>>(content);
                if (preloadList != null)
                {
                    for (var i = 0; i < preloadList.Count; i++)
                    {
                        this.AddOrUpdate(preloadList[i].Key, preloadList[i].Value, i);
                    }
                }

                this.isCacheValid = false;
            }
        }

        public int Count() => this.preloads.Count;

        public void Save(string filepathname)
        {
            var dataToSave = new List<KeyValuePair<string, PreloadInfo>>();
            foreach (var key in this.GetUpToDateCache())
            {
                dataToSave.Add(new KeyValuePair<string, PreloadInfo>(key, this.preloads[this.indexes[key]]));
            }

            var preloadsDataString = JsonConvert.SerializeObject(dataToSave, Formatting.Indented);
            File.WriteAllText(filepathname, preloadsDataString);
        }

        public List<string> GetUpToDateCache()
        {
            if (!this.isCacheValid)
            {
                this.keysCache = this.indexes.OrderBy(index => index.Value).Select(selector => selector.Key).ToList();
            }

            return this.keysCache;
        }
    }
}
