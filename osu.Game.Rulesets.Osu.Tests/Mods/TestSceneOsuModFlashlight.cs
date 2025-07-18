// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Replays;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    public partial class TestSceneOsuModFlashlight : OsuModTestScene
    {
        [TestCase(600)]
        [TestCase(120)]
        [TestCase(1200)]
        public void TestFollowDelay(double followDelay) => CreateModTest(new ModTestData { Mod = new OsuModFlashlight { FollowDelay = { Value = followDelay } }, PassCondition = () => true });

        [TestCase(1f)]
        [TestCase(0.5f)]
        [TestCase(1.5f)]
        [TestCase(2f)]
        public void TestSizeMultiplier(float sizeMultiplier) => CreateModTest(new ModTestData { Mod = new OsuModFlashlight { SizeMultiplier = { Value = sizeMultiplier } }, PassCondition = () => true });

        [Test]
        public void TestComboBasedSize([Values] bool comboBasedSize) => CreateModTest(new ModTestData { Mod = new OsuModFlashlight { ComboBasedSize = { Value = comboBasedSize } }, PassCondition = () => true });

        [Test]
        public void TestPlayfieldBasedSize()
        {
            OsuModFlashlight flashlight;
            CreateModTest(new ModTestData
            {
                Mods = [flashlight = new OsuModFlashlight(), new OsuModBarrelRoll()],
                PassCondition = () =>
                {
                    var flashlightOverlay = Player.DrawableRuleset.Overlays
                                                  .ChildrenOfType<ModFlashlight<OsuHitObject>.Flashlight>()
                                                  .First();

                    // the combo check is here because the flashlight radius decreases for the first time at 100 combo
                    // and hardcoding it here eliminates the need to meddle in flashlight internals further by e.g. exposing `GetComboScaleFor()`
                    return flashlightOverlay.GetSize() < flashlight.DefaultFlashlightSize && Player.GameplayState.ScoreProcessor.Combo.Value < 100;
                }
            });
        }

        [Test]
        public void TestSliderDimsOnlyAfterStartTime()
        {
            bool sliderDimmedBeforeStartTime = false;

            CreateModTest(new ModTestData
            {
                Mod = new OsuModFlashlight(),
                PassCondition = () =>
                {
                    sliderDimmedBeforeStartTime |=
                        Player.GameplayClockContainer.CurrentTime < 1000 && Player.ChildrenOfType<ModFlashlight<OsuHitObject>.Flashlight>().Single().FlashlightDim > 0;
                    return Player.GameplayState.HasPassed && !sliderDimmedBeforeStartTime;
                },
                CreateBeatmap = () => new OsuBeatmap
                {
                    HitObjects = new List<OsuHitObject>
                    {
                        new HitCircle { StartTime = 0, },
                        new Slider
                        {
                            StartTime = 1000,
                            Path = new SliderPath(new[]
                            {
                                new PathControlPoint(),
                                new PathControlPoint(new Vector2(100))
                            })
                        }
                    },
                    StackLeniency = 0,
                },
                ReplayFrames = new List<ReplayFrame>
                {
                    new OsuReplayFrame(0, new Vector2(), OsuAction.LeftButton),
                    new OsuReplayFrame(990, new Vector2()),
                    new OsuReplayFrame(1000, new Vector2(), OsuAction.LeftButton),
                    new OsuReplayFrame(2000, new Vector2(100), OsuAction.LeftButton),
                    new OsuReplayFrame(2001, new Vector2(100)),
                },
                Autoplay = false,
            });
        }

        [Test]
        public void TestSliderDoesDimAfterStartTimeIfHitEarly()
        {
            bool sliderDimmed = false;

            CreateModTest(new ModTestData
            {
                Mod = new OsuModFlashlight(),
                PassCondition = () =>
                {
                    sliderDimmed |=
                        Player.GameplayClockContainer.CurrentTime >= 1000 && Player.ChildrenOfType<ModFlashlight<OsuHitObject>.Flashlight>().Single().FlashlightDim > 0;
                    return Player.GameplayState.HasPassed && sliderDimmed;
                },
                CreateBeatmap = () => new OsuBeatmap
                {
                    HitObjects = new List<OsuHitObject>
                    {
                        new Slider
                        {
                            StartTime = 1000,
                            Path = new SliderPath(new[]
                            {
                                new PathControlPoint(),
                                new PathControlPoint(new Vector2(100))
                            })
                        }
                    },
                },
                ReplayFrames = new List<ReplayFrame>
                {
                    new OsuReplayFrame(990, new Vector2(), OsuAction.LeftButton),
                    new OsuReplayFrame(2000, new Vector2(100), OsuAction.LeftButton),
                    new OsuReplayFrame(2001, new Vector2(100)),
                },
                Autoplay = false,
            });
        }

        [Test]
        public void TestSliderDoesDimAfterStartTimeIfHitLate()
        {
            bool sliderDimmed = false;

            CreateModTest(new ModTestData
            {
                Mod = new OsuModFlashlight(),
                PassCondition = () =>
                {
                    sliderDimmed |=
                        Player.GameplayClockContainer.CurrentTime >= 1000 && Player.ChildrenOfType<ModFlashlight<OsuHitObject>.Flashlight>().Single().FlashlightDim > 0;
                    return Player.GameplayState.HasPassed && sliderDimmed;
                },
                CreateBeatmap = () => new OsuBeatmap
                {
                    HitObjects = new List<OsuHitObject>
                    {
                        new Slider
                        {
                            StartTime = 1000,
                            Path = new SliderPath(new[]
                            {
                                new PathControlPoint(),
                                new PathControlPoint(new Vector2(100))
                            })
                        }
                    },
                },
                ReplayFrames = new List<ReplayFrame>
                {
                    new OsuReplayFrame(1100, new Vector2(), OsuAction.LeftButton),
                    new OsuReplayFrame(2000, new Vector2(100), OsuAction.LeftButton),
                    new OsuReplayFrame(2001, new Vector2(100)),
                },
                Autoplay = false,
            });
        }
    }
}
