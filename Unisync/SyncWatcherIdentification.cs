namespace Unisync
{
	public struct SyncWatcherIdentification
	{
		public string Tag { get; private set; }
        public string Group { get; private set; }
        public Guid ID { get; private set; }

        public SyncWatcherIdentification(string tag, string group)
        {
            Tag = tag;
            Group = group;
            ID = Guid.NewGuid();
        }

        public override string ToString()
        {
            return Tag;
        }
    }
}
