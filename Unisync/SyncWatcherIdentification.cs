namespace Unisync
{
	public struct SyncWatcherIdentification
	{
		public string Tag { get; private set; }
        public Guid ID { get; private set; }

        public SyncWatcherIdentification(string tag)
        {
            Tag = tag;
            ID = Guid.NewGuid();
        }

        public override string ToString()
        {
            return Tag;
        }
    }
}
