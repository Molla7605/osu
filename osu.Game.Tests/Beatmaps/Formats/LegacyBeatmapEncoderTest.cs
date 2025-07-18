﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.Beatmaps.Legacy;
using osu.Game.IO;
using osu.Game.IO.Serialization;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Taiko;
using osu.Game.Skinning;
using osu.Game.Storyboards;
using osu.Game.Tests.Resources;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.Beatmaps.Formats
{
    [TestFixture]
    public class LegacyBeatmapEncoderTest
    {
        private static readonly DllResourceStore beatmaps_resource_store = TestResources.GetStore();

        private static IEnumerable<string> allBeatmaps = beatmaps_resource_store.GetAvailableResources().Where(res => res.EndsWith(".osu", StringComparison.Ordinal));

        [Test]
        public void TestUnsupportedStoryboardEvents()
        {
            const string name = "Resources/storyboard_only_video.osu";

            var decoded = decodeFromLegacy(beatmaps_resource_store.GetStream(name), name);
            Assert.That(decoded.beatmap.UnhandledEventLines.Count, Is.EqualTo(1));
            Assert.That(decoded.beatmap.UnhandledEventLines.Single(), Is.EqualTo("Video,0,\"video.avi\""));

            var memoryStream = encodeToLegacy(decoded);

            var storyboard = new LegacyStoryboardDecoder().Decode(new LineBufferedReader(memoryStream));
            StoryboardLayer video = storyboard.Layers.Single(l => l.Name == "Video");
            Assert.That(video.Elements.Count, Is.EqualTo(1));
        }

        [TestCaseSource(nameof(allBeatmaps))]
        public void TestEncodeDecodeStability(string name)
        {
            var decoded = decodeFromLegacy(beatmaps_resource_store.GetStream(name), name);
            var decodedAfterEncode = decodeFromLegacy(encodeToLegacy(decoded), name);

            sort(decoded.beatmap);
            sort(decodedAfterEncode.beatmap);

            compareBeatmaps(decoded, decodedAfterEncode);
        }

        [TestCaseSource(nameof(allBeatmaps))]
        public void TestEncodeDecodeStabilityDoubleConvert(string name)
        {
            var decoded = decodeFromLegacy(beatmaps_resource_store.GetStream(name), name);
            var decodedAfterEncode = decodeFromLegacy(encodeToLegacy(decoded), name);

            // run an extra convert. this is expected to be stable.
            decodedAfterEncode.beatmap = convert(decodedAfterEncode.beatmap);

            sort(decoded.beatmap);
            sort(decodedAfterEncode.beatmap);

            compareBeatmaps(decoded, decodedAfterEncode);
        }

        [TestCaseSource(nameof(allBeatmaps))]
        public void TestEncodeDecodeStabilityWithNonLegacyControlPoints(string name)
        {
            var decoded = decodeFromLegacy(beatmaps_resource_store.GetStream(name), name);

            // we are testing that the transfer of relevant data to hitobjects (from legacy control points) sticks through encode/decode.
            // before the encode step, the legacy information is removed here.
            decoded.beatmap.ControlPointInfo = removeLegacyControlPointTypes(decoded.beatmap.ControlPointInfo);

            var decodedAfterEncode = decodeFromLegacy(encodeToLegacy(decoded), name);

            compareBeatmaps(decoded, decodedAfterEncode);

            static ControlPointInfo removeLegacyControlPointTypes(ControlPointInfo controlPointInfo)
            {
                // emulate non-legacy control points by cloning the non-legacy portion.
                // the assertion is that the encoder can recreate this losslessly from hitobject data.
                Assert.IsInstanceOf<LegacyControlPointInfo>(controlPointInfo);

                var newControlPoints = new ControlPointInfo();

                foreach (var point in controlPointInfo.AllControlPoints)
                {
                    // completely ignore "legacy" types, which have been moved to HitObjects.
                    // even though these would mostly be ignored by the Add call, they will still be available in groups,
                    // which isn't what we want to be testing here.
                    if (point is SampleControlPoint || point is DifficultyControlPoint)
                        continue;

                    newControlPoints.Add(point.Time, point.DeepClone());
                }

                return newControlPoints;
            }
        }

        private void compareBeatmaps((IBeatmap beatmap, TestLegacySkin skin) expected, (IBeatmap beatmap, TestLegacySkin skin) actual)
        {
            // Check all control points that are still considered to be at a global level.
            Assert.That(actual.beatmap.ControlPointInfo.TimingPoints.Serialize(), Is.EqualTo(expected.beatmap.ControlPointInfo.TimingPoints.Serialize()));
            Assert.That(actual.beatmap.ControlPointInfo.EffectPoints.Serialize(), Is.EqualTo(expected.beatmap.ControlPointInfo.EffectPoints.Serialize()));

            // Check all hitobjects.
            Assert.That(actual.beatmap.HitObjects.Serialize(), Is.EqualTo(expected.beatmap.HitObjects.Serialize()));

            // Check skin.
            Assert.IsTrue(areComboColoursEqual(expected.skin.Configuration, actual.skin.Configuration));
        }

        [Test]
        public void TestEncodeBSplineCurveType()
        {
            var beatmap = new Beatmap
            {
                HitObjects =
                {
                    new Slider
                    {
                        Path = new SliderPath(new[]
                        {
                            new PathControlPoint(Vector2.Zero, PathType.BSpline(3)),
                            new PathControlPoint(new Vector2(50)),
                            new PathControlPoint(new Vector2(100), PathType.BSpline(3)),
                            new PathControlPoint(new Vector2(150))
                        })
                    },
                }
            };

            var decodedAfterEncode = decodeFromLegacy(encodeToLegacy((beatmap, new TestLegacySkin(beatmaps_resource_store, string.Empty))), string.Empty);
            var decodedSlider = (Slider)decodedAfterEncode.beatmap.HitObjects[0];
            Assert.That(decodedSlider.Path.ControlPoints.Count, Is.EqualTo(4));
            Assert.That(decodedSlider.Path.ControlPoints[0].Type, Is.EqualTo(PathType.BSpline(3)));
            Assert.That(decodedSlider.Path.ControlPoints[2].Type, Is.EqualTo(PathType.BSpline(3)));
        }

        [Test]
        public void TestEncodeMultiSegmentSliderWithFloatingPointError()
        {
            var beatmap = new Beatmap
            {
                HitObjects =
                {
                    new Slider
                    {
                        Position = new Vector2(0.6f),
                        Path = new SliderPath(new[]
                        {
                            new PathControlPoint(Vector2.Zero, PathType.BEZIER),
                            new PathControlPoint(new Vector2(0.5f)),
                            new PathControlPoint(new Vector2(0.51f)), // This is actually on the same position as the previous one in legacy beatmaps (truncated to int).
                            new PathControlPoint(new Vector2(1f), PathType.BEZIER),
                            new PathControlPoint(new Vector2(2f))
                        })
                    },
                }
            };

            var decodedAfterEncode = decodeFromLegacy(encodeToLegacy((beatmap, new TestLegacySkin(beatmaps_resource_store, string.Empty))), string.Empty);
            var decodedSlider = (Slider)decodedAfterEncode.beatmap.HitObjects[0];
            Assert.That(decodedSlider.Path.ControlPoints.Count, Is.EqualTo(5));
        }

        [Test]
        public void TestOnlyEightComboColoursEncoded()
        {
            var beatmapSkin = new LegacyBeatmapSkin(new BeatmapInfo(), null)
            {
                Configuration =
                {
                    CustomComboColours =
                    {
                        new Color4(1, 1, 1, 255),
                        new Color4(2, 2, 2, 255),
                        new Color4(3, 3, 3, 255),
                        new Color4(4, 4, 4, 255),
                        new Color4(5, 5, 5, 255),
                        new Color4(6, 6, 6, 255),
                        new Color4(7, 7, 7, 255),
                        new Color4(8, 8, 8, 255),
                        new Color4(9, 9, 9, 255),
                    }
                }
            };

            var decodedAfterEncode = decodeFromLegacy(encodeToLegacy((new Beatmap(), beatmapSkin)), string.Empty);
            Assert.That(decodedAfterEncode.skin.Configuration.CustomComboColours, Has.Count.EqualTo(8));
        }

        [Test]
        public void TestEncodeStabilityOfSliderWithFractionalCoordinates()
        {
            Slider originalSlider = new Slider
            {
                Position = new Vector2(0.6f),
                Path = new SliderPath(new[]
                {
                    new PathControlPoint(Vector2.Zero, PathType.PERFECT_CURVE),
                    new PathControlPoint(new Vector2(25.6f, 78.4f)),
                    new PathControlPoint(new Vector2(55.8f, 34.2f)),
                })
            };
            var beatmap = new Beatmap
            {
                HitObjects = { originalSlider }
            };

            var encoded = encodeToLegacy((beatmap, new TestLegacySkin(beatmaps_resource_store, string.Empty)));
            var decodedAfterEncode = decodeFromLegacy(encoded, string.Empty, version: LegacyBeatmapEncoder.FIRST_LAZER_VERSION);
            var decodedSlider = (Slider)decodedAfterEncode.beatmap.HitObjects[0];
            Assert.That(decodedSlider.Path.ControlPoints.Select(p => p.Position),
                Is.EquivalentTo(originalSlider.Path.ControlPoints.Select(p => p.Position)));
        }

        private bool areComboColoursEqual(IHasComboColours a, IHasComboColours b)
        {
            // equal to null, no need to SequenceEqual
            if (a.ComboColours == null && b.ComboColours == null)
                return true;

            if (a.ComboColours == null || b.ComboColours == null)
                return false;

            return a.ComboColours.SequenceEqual(b.ComboColours);
        }

        private void sort(IBeatmap beatmap)
        {
            // Sort control points to ensure a sane ordering, as they may be parsed in different orders. This works because each group contains only uniquely-typed control points.
            foreach (var g in beatmap.ControlPointInfo.Groups)
            {
                ArrayList.Adapter((IList)g.ControlPoints).Sort(
                    Comparer<ControlPoint>.Create((c1, c2) => string.Compare(c1.GetType().ToString(), c2.GetType().ToString(), StringComparison.Ordinal)));
            }
        }

        private (IBeatmap beatmap, TestLegacySkin skin) decodeFromLegacy(Stream stream, string name, int version = LegacyDecoder<Beatmap>.LATEST_VERSION)
        {
            using (var reader = new LineBufferedReader(stream))
            {
                var beatmap = new LegacyBeatmapDecoder(version) { ApplyOffsets = false }.Decode(reader);
                var beatmapSkin = new TestLegacySkin(beatmaps_resource_store, name);
                stream.Seek(0, SeekOrigin.Begin);
                beatmapSkin.Configuration = new LegacySkinDecoder().Decode(reader);
                return (convert(beatmap), beatmapSkin);
            }
        }

        private class TestLegacySkin : LegacySkin
        {
            public TestLegacySkin(IResourceStore<byte[]> fallbackStore, string fileName)
                : base(new SkinInfo { Name = "Test Skin", Creator = "Craftplacer" }, null, fallbackStore, fileName)
            {
            }
        }

        private MemoryStream encodeToLegacy((IBeatmap beatmap, ISkin skin) fullBeatmap)
        {
            var (beatmap, beatmapSkin) = fullBeatmap;
            var stream = new MemoryStream();

            using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
                new LegacyBeatmapEncoder(beatmap, beatmapSkin).Encode(writer);

            stream.Position = 0;

            return stream;
        }

        private IBeatmap convert(IBeatmap beatmap)
        {
            switch (beatmap.BeatmapInfo.Ruleset.OnlineID)
            {
                case 0:
                    beatmap.BeatmapInfo.Ruleset = new OsuRuleset().RulesetInfo;
                    break;

                case 1:
                    beatmap.BeatmapInfo.Ruleset = new TaikoRuleset().RulesetInfo;
                    break;

                case 2:
                    beatmap.BeatmapInfo.Ruleset = new CatchRuleset().RulesetInfo;
                    break;

                case 3:
                    beatmap.BeatmapInfo.Ruleset = new ManiaRuleset().RulesetInfo;
                    break;
            }

            return new TestWorkingBeatmap(beatmap).GetPlayableBeatmap(beatmap.BeatmapInfo.Ruleset);
        }

        private class TestWorkingBeatmap : WorkingBeatmap
        {
            private readonly IBeatmap beatmap;

            public TestWorkingBeatmap(IBeatmap beatmap)
                : base(beatmap.BeatmapInfo, null)
            {
                this.beatmap = beatmap;
            }

            protected override IBeatmap GetBeatmap() => beatmap;

            public override Texture GetBackground() => throw new NotImplementedException();

            protected override Track GetBeatmapTrack() => throw new NotImplementedException();

            protected internal override ISkin GetSkin() => throw new NotImplementedException();

            public override Stream GetStream(string storagePath) => throw new NotImplementedException();
        }
    }
}
