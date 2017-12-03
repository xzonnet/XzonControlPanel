using System.Collections.Generic;
using System.Configuration;

namespace XzonControlPanel.Config
{
    public class MinersSection : ConfigurationSection
    {

        [ConfigurationProperty("Miners", IsDefaultCollection = false)]
        [ConfigurationCollection(typeof(MinerCollection),AddItemName = "add",ClearItemsName = "clear",RemoveItemName = "remove")]
        public MinerCollection Miners => (MinerCollection)base["Miners"];
    }

    public class MinerConfig : ConfigurationElement
    {
        public MinerConfig() { }

        public MinerConfig(string exeLocation, string commandLineParameters, decimal ratio)
        {
            ExeLocation = exeLocation;
            CommandLineParameters = commandLineParameters;
            Ratio = ratio;
        }

        [ConfigurationProperty("name", IsRequired = true)]
        public string Name
        {
            get { return (string)this["name"]; }
            set { this["name"] = value; }
        }

        [ConfigurationProperty("exeLocation", IsRequired = true)]
        public string ExeLocation
        {
            get { return (string)this["exeLocation"]; }
            set { this["exeLocation"] = value; }
        }

        [ConfigurationProperty("commandLineParameters", IsRequired = true)]
        public string CommandLineParameters
        {
            get { return (string)this["commandLineParameters"]; }
            set { this["commandLineParameters"] = value; }
        }

        [ConfigurationProperty("ratio", IsRequired = true)]
        public decimal Ratio
        {
            get { return (decimal)this["ratio"]; }
            set { this["ratio"] = value; }
        }

        [ConfigurationProperty("currencyCode", IsRequired = false)]
        public string CurrencyCode
        {
            get { return (string)this["currencyCode"]; }
            set { this["currencyCode"] = value; }
        }

        [ConfigurationProperty("hashrateWarning", IsRequired = true)]
        public decimal HashrateWarning
        {
            get { return (decimal)this["hashrateWarning"]; }
            set { this["hashrateWarning"] = value; }
        }

        [ConfigurationProperty("hashrateError", IsRequired = true)]
        public decimal HashrateError
        {
            get { return (decimal)this["hashrateError"]; }
            set { this["hashrateError"] = value; }
        }

        public decimal TrueRatio { get; set; }

        public int StartMinute { get; set; }
        public string Wallet { get; set; }
    }

    public class MinerCollection : ConfigurationElementCollection, IEnumerable<MinerConfig>
    {
        private int _position;
        public MinerConfig this[int index]
        {
            get { return (MinerConfig)BaseGet(index); }
            set
            {
                if (BaseGet(index) != null)
                {
                    BaseRemoveAt(index);
                }
                BaseAdd(index, value);
            }
        }

        public void Add(MinerConfig minerConfig)
        {
            BaseAdd(minerConfig);
        }

        public void Clear()
        {
            BaseClear();
        }

        protected override ConfigurationElement CreateNewElement()
        {
            return new MinerConfig();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((MinerConfig)element).Name;
        }

        public void Remove(MinerConfig serviceConfig)
        {
            BaseRemove(serviceConfig.Name);
        }

        public void RemoveAt(int index)
        {
            BaseRemoveAt(index);
        }

        public void Remove(string name)
        {
            BaseRemove(name);
        }

        public override bool IsReadOnly()
        {
            return false;
        }

        public bool MoveNext()
        {
            _position++;
            return (_position < Count);
        }
        public void Reset()
        {
            _position = 0;
        }
        public MinerConfig Current => this[_position];

        public new IEnumerator<MinerConfig> GetEnumerator()
        {
            foreach (var key in BaseGetAllKeys())
            {
                yield return (MinerConfig)BaseGet(key);
            }
        }

        #region IDisposable Support
        private bool _disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Clear();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}