using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    /// <summary>
    /// Absolute-elevation display must not silently flatten authored relief. The mass navigation
    /// large-world relief peaks at ~417 m; with the default 36 m peak span every real vertex lands
    /// outside [0, span] and used to collapse to the sea plane, leaving grounded agents floating.
    /// Void-ceiling flattening is a per-asset contract (profile-declared): exporter-derived uint16
    /// assets (crowd maps, scale 0..140000cm over the raw range) spend the whole range on authored
    /// relief, so their high samples must keep elevation unless the map declares ocean void fills.
    /// </summary>
    [TestFixture]
    public sealed class ContinuousHeightmapAbsoluteDisplayHeightTests
    {
        private const float MassNavigationPeakCm = 41661f;
        private const float MassNavigationOriginCm = 28770f;
        private const float IdentitySentinelCm = 57_671f;

        [Test]
        public void AuthoredReliefAboveDefaultSpanKeepsItsElevation()
        {
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                MassNavigationOriginCm,
                seaLevelCm: 0f,
                oceanVoidSentinelCm: null);

            Assert.That(display, Is.EqualTo(MassNavigationOriginCm),
                "Authored relief must keep its height even when it exceeds the tint peak span.");
        }

        [Test]
        public void AuthoredReliefPeakKeepsItsElevation()
        {
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                MassNavigationPeakCm,
                seaLevelCm: 0f,
                oceanVoidSentinelCm: null);

            Assert.That(display, Is.EqualTo(MassNavigationPeakCm));
        }

        [Test]
        public void ScaledCeilingRangePeakKeepsItsElevation()
        {
            // crowd 地图形态:满量程映射 0..140000cm,真实峰顶在码字天花板。哨兵未声明时
            // 高于任何历史 cm 域常数的样本都必须保高——cm 域阈值把它们压回海平面就是
            // "平顶坑洞"缺陷本体。
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                140_000f,
                seaLevelCm: 41_718f,
                oceanVoidSentinelCm: null);

            Assert.That(display, Is.EqualTo(140_000f),
                "Exporter-derived scaled assets use the raw ceiling for real peaks; undeclared maps must not flatten them.");
        }

        [Test]
        public void SubmergedDepthCollapsesToSeaPlane()
        {
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                -8200f,
                seaLevelCm: 0f,
                oceanVoidSentinelCm: IdentitySentinelCm);

            Assert.That(display, Is.EqualTo(0f),
                "Below-sea samples stay on the sea plane so continental pits do not excavate.");
        }

        [Test]
        public void DeclaredVoidSentinelCollapsesToSeaPlane()
        {
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                65_535f,
                seaLevelCm: 0f,
                oceanVoidSentinelCm: IdentitySentinelCm);

            Assert.That(display, Is.EqualTo(0f),
                "Declared void/ocean sentinels far above the authored span stay on the sea plane.");
        }

        [Test]
        public void SentinelDetectionUsesAbsoluteOvershootNotRelativeSpan()
        {
            // A tight span must not turn ordinary relief into a sentinel.
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                MassNavigationOriginCm,
                seaLevelCm: 0f,
                oceanVoidSentinelCm: IdentitySentinelCm);

            Assert.That(display, Is.EqualTo(MassNavigationOriginCm));
        }

        [Test]
        public void ResolveOceanVoidSentinelCm_FollowsAssetScaleAndDeclaration()
        {
            // identity 缩放(原码=厘米):历史 0.88×65535 阈值逐位保持
            var identity = new ContinuousHeightSampleScale(0, 1, 1);
            Assert.That(
                RaylibContinuousHeightmapRenderer.ResolveOceanVoidSentinelCm(
                    true, CreateScaledChunk(identity)),
                Is.EqualTo(57_671f));

            // crowd 形态缩放(0..140000cm 满量程):阈值随 scale 抬到 ~1233m,不再误伤 576.7m+ 的真实山体
            var crowd = new ContinuousHeightSampleScale(0, 140_000, 65_535);
            Assert.That(
                RaylibContinuousHeightmapRenderer.ResolveOceanVoidSentinelCm(
                    true, CreateScaledChunk(crowd)),
                Is.EqualTo(57_671f * 140_000f / 65_535f).Within(0.01f));

            // 未声明 → null(不压平);int16 布局无原码天花板语义 → null
            Assert.That(
                RaylibContinuousHeightmapRenderer.ResolveOceanVoidSentinelCm(false, CreateScaledChunk(identity)),
                Is.Null);
            Assert.That(
                RaylibContinuousHeightmapRenderer.ResolveOceanVoidSentinelCm(true, CreateInt16Chunk()),
                Is.Null);
        }

        [Test]
        public void RenderProfileClone_PreservesOceanVoidFillFlag()
        {
            var profile = ContinuousHeightmapRenderProfile.CreateDefault();
            Assert.That(profile.OceanVoidFillAtSampleCeiling, Is.False, "flag must default off for exporter-derived assets");
            profile.OceanVoidFillAtSampleCeiling = true;
            Assert.That(profile.NormalizeAndValidate().OceanVoidFillAtSampleCeiling, Is.True);
        }

        private static ContinuousHeightmapRenderChunk CreateScaledChunk(ContinuousHeightSampleScale scale)
        {
            ushort[] samples = new ushort[4];
            return new ContinuousHeightmapRenderChunk(
                0, 0, new WorldAabbCm(0, 0, 300, 300),
                2, 2, 300f, 300f,
                heightSamplesCm: default,
                heightSamplesRaw: samples.AsMemory(),
                sampleScale: scale,
                storageLayout: ContinuousHeightmapStorageLayout.RowMajorUInt16Scaled,
                sampleStride: 2, layerSampleOffset: 0, revision: 1);
        }

        private static ContinuousHeightmapRenderChunk CreateInt16Chunk()
        {
            short[] samples = new short[4];
            return new ContinuousHeightmapRenderChunk(
                0, 0, new WorldAabbCm(0, 0, 300, 300),
                2, 2, 300f, 300f,
                heightSamplesCm: samples.AsMemory(),
                heightSamplesRaw: default,
                sampleScale: ContinuousHeightSampleScale.IdentityCentimeters,
                storageLayout: ContinuousHeightmapStorageLayout.RowMajorInt16Centimeters,
                sampleStride: 2, layerSampleOffset: 0, revision: 1);
        }
    }
}
