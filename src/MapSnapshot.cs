namespace Astonia.Editor
{
    public static class MapSnapshot
    {
        public static void Export(MapDocument document, AssetCatalog assets, string path, int width = 4096, bool smooth = false, bool finestArtwork = true, int nativeScale = 0)
        { TiledMapExport.Overview(document, assets, path, width, smooth, finestArtwork, nativeScale); }
    }
}
