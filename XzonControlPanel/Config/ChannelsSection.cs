using System.Collections.Generic;
using System.Configuration;
using XzonControlPanel.Security;

namespace XzonControlPanel.Config
{
    public class ChannelsSection : ConfigurationSection
    {

        [ConfigurationProperty("Channels", IsDefaultCollection = false)]
        [ConfigurationCollection(typeof(ChannelCollection), AddItemName = "add", ClearItemsName = "clear", RemoveItemName = "remove")]
        public ChannelCollection Channels => (ChannelCollection)base["Channels"];
    }

    public class ChannelConfig : ConfigurationElement
    {
        public ChannelConfig() { }

        public ChannelConfig(string name, string password)
        {
            Name = name;
            Password = password;
        }

        [ConfigurationProperty("name", IsRequired = true)]
        public string Name
        {
            get { return (string)this["name"]; }
            set { this["name"] = value; }
        }

        [ConfigurationProperty("password", IsRequired = true)]
        public string Password
        {
            get { return SecurityHelper.GetHashSha256((string) this["password"]); }
            set { this["password"] = SecurityHelper.GetHashSha256(value); }
        }

        [ConfigurationProperty("showRigName", IsRequired = true)]
        public bool ShowRigName
        {
            get { return (bool)this["showRigName"]; }
            set { this["showRigName"] = value; }
        }

        [ConfigurationProperty("isTrustedChannel", IsRequired = true)]
        public bool IsTrustedChannel
        {
            get { return (bool)this["isTrustedChannel"]; }
            set { this["isTrustedChannel"] = value; }
        }

        [ConfigurationProperty("showWalletAddress", IsRequired = true)]
        public bool ShowWalletAddress
        {
            get { return (bool)this["showWalletAddress"]; }
            set { this["showWalletAddress"] = value; }
        }

        [ConfigurationProperty("showMiningPool", IsRequired = true)]
        public bool ShowMiningPool
        {
            get { return (bool)this["showMiningPool"]; }
            set { this["showMiningPool"] = value; }
        }

        [ConfigurationProperty("showSchedule", IsRequired = true)]
        public bool ShowSchedule
        {
            get { return (bool)this["showSchedule"]; }
            set { this["showSchedule"] = value; }
        }
    }

    public class ChannelCollection : ConfigurationElementCollection, IEnumerable<ChannelConfig>
    {
        private int _position;
        public ChannelConfig this[int index]
        {
            get { return (ChannelConfig)BaseGet(index); }
            set
            {
                if (BaseGet(index) != null)
                {
                    BaseRemoveAt(index);
                }
                BaseAdd(index, value);
            }
        }

        public void Add(ChannelConfig channelConfig)
        {
            BaseAdd(channelConfig);
        }

        public void Clear()
        {
            BaseClear();
        }

        protected override ConfigurationElement CreateNewElement()
        {
            return new ChannelConfig();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((ChannelConfig)element).Name;
        }

        public void Remove(ChannelConfig serviceConfig)
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
        public ChannelConfig Current => this[_position];

        public new IEnumerator<ChannelConfig> GetEnumerator()
        {
            foreach (var key in BaseGetAllKeys())
            {
                yield return (ChannelConfig)BaseGet(key);
            }
        }

        #region IDisposable Support
        private bool _disposedValue; // To detect redundant calls

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