using NUnit.Framework;
using UnityEngine;

namespace NavalCommander.Presentation.Rules.Tests
{
    public sealed class HudScaleTests
    {
        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        public void ReferenceSizeAndLarger_KeepTheLayoutUnscaled(int width, int height)
        {
            Assert.That(HudScale.For(width, height), Is.EqualTo(1f));
        }

        [TestCase(640, 360, 0.5f)]
        [TestCase(960, 720, 0.75f)]
        [TestCase(1280, 360, 0.5f)]
        public void SmallWindows_ScaleByTheTighterSide(int width, int height, float expected)
        {
            Assert.That(HudScale.For(width, height), Is.EqualTo(expected).Within(0.0001f));
        }
    }

    public sealed class MenuScreenStateTests
    {
        [Test]
        public void Idle_ShowsOnlyTheEnabledSearchButton()
        {
            MenuScreenState state = MenuScreenState.For(false, false);

            Assert.That(state.ShowIdlePanel, Is.True);
            Assert.That(state.SearchButtonInteractable, Is.True);
            Assert.That(state.ShowSearchingPanel, Is.False);
            Assert.That(state.ShowWaitingPanel, Is.False);
        }

        [Test]
        public void SearchRunning_KeepsTheSearchButtonVisibleButDisabled()
        {
            MenuScreenState state = MenuScreenState.For(false, true);

            Assert.That(state.ShowIdlePanel, Is.True);
            Assert.That(state.SearchButtonInteractable, Is.False);
            Assert.That(state.ShowSearchingPanel, Is.True);
            Assert.That(state.ShowWaitingPanel, Is.False);
        }

        [Test]
        public void ConnectedWhileTheSearchRuns_DoesNotOfferTheWaitingRoomYet()
        {
            MenuScreenState state = MenuScreenState.For(true, true);

            Assert.That(state.ShowWaitingPanel, Is.False);
            Assert.That(state.ShowSearchingPanel, Is.True);
            Assert.That(state.SearchButtonInteractable, Is.False);
        }

        [Test]
        public void ConnectedAfterTheSearch_ShowsOnlyTheWaitingRoom()
        {
            MenuScreenState state = MenuScreenState.For(true, false);

            Assert.That(state.ShowWaitingPanel, Is.True);
            Assert.That(state.ShowIdlePanel, Is.False);
            Assert.That(state.ShowSearchingPanel, Is.False);
        }
    }

    public sealed class MatchResultMemoTests
    {
        [Test]
        public void BeforeTheMatchEnds_ThereIsNoResultToKeep()
        {
            var memo = new MatchResultMemo();
            memo.RememberLocalSlot(1);

            Assert.That(memo.HasResult, Is.False);
            Assert.That(memo.XpLine(86, 3, 10, 300), Is.Null);
        }

        [Test]
        public void Winner_KeepsResultAndXpLineWithoutFurtherObservations()
        {
            var memo = new MatchResultMemo();
            memo.RememberLocalSlot(1);
            memo.RememberFinish("", 1);

            Assert.That(memo.HasResult, Is.True);
            Assert.That(memo.ResultText, Is.EqualTo("¡Ganó el slot 1!"));
            Assert.That(memo.XpLine(86, 3, 10, 300), Is.EqualTo("+86 XP | Nivel 3 (10/300)"));
        }

        [Test]
        public void Loser_SeesTheResultButNotAnEarlierVictoryXp()
        {
            var memo = new MatchResultMemo();
            memo.RememberLocalSlot(0);
            memo.RememberFinish("", 1);

            Assert.That(memo.ResultText, Is.EqualTo("¡Ganó el slot 1!"));
            Assert.That(memo.XpLine(86, 3, 10, 300), Is.Null);
        }

        [Test]
        public void NoWinner_ReportsADraw()
        {
            var memo = new MatchResultMemo();
            memo.RememberLocalSlot(0);
            memo.RememberFinish("", -1);

            Assert.That(memo.ResultText, Is.EqualTo("Empate / sin ganador"));
            Assert.That(memo.XpLine(86, 3, 10, 300), Is.Null);
        }

        [Test]
        public void Failure_ReportsTheErrorAndNoXp()
        {
            var memo = new MatchResultMemo();
            memo.RememberLocalSlot(1);
            memo.RememberFinish("se cayó", 1);

            Assert.That(memo.ResultText, Is.EqualTo("La partida terminó con un error: se cayó"));
            Assert.That(memo.XpLine(86, 3, 10, 300), Is.Null);
        }

        [Test]
        public void WinnerWithoutGain_ShowsNoXpLine()
        {
            var memo = new MatchResultMemo();
            memo.RememberLocalSlot(1);
            memo.RememberFinish("", 1);

            Assert.That(memo.XpLine(0, 3, 10, 300), Is.Null);
        }
    }

    public sealed class BoardPresenceTests
    {
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Next_WithoutConnection_IsNotDrawn(bool wasDrawn, bool hasShips)
        {
            Assert.That(BoardPresence.Next(wasDrawn, false, hasShips), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Next_ConnectedWithShips_IsDrawn(bool wasDrawn)
        {
            Assert.That(BoardPresence.Next(wasDrawn, true, true), Is.True);
        }

        [Test]
        public void Next_ConnectedBeforeTheShipsArrive_IsNotDrawnYet()
        {
            Assert.That(BoardPresence.Next(false, true, false), Is.False);
        }

        [Test]
        public void Next_ConnectedWhileTheShipListIsEmpty_KeepsTheBoardAlreadyDrawn()
        {
            Assert.That(BoardPresence.Next(true, true, false), Is.True);
        }

        [Test]
        public void Next_AFollowingMatch_DrawsTheBoardAgainAfterTheDisconnection()
        {
            bool afterDisconnection = BoardPresence.Next(true, false, false);
            bool afterConnecting = BoardPresence.Next(afterDisconnection, true, false);
            bool afterShipsArrive = BoardPresence.Next(afterConnecting, true, true);

            Assert.That(afterDisconnection, Is.False);
            Assert.That(afterConnecting, Is.False);
            Assert.That(afterShipsArrive, Is.True);
        }
    }

    public sealed class HudPanelLayoutTests
    {
        [TestCase(720f, 712f)]
        [TestCase(1080f, 1072f)]
        public void Area_StartsAtTheCornerAndLeavesTheSameGapBelow(float hudHeight, float expectedHeight)
        {
            Rect area = HudPanelLayout.Area(hudHeight);

            Assert.That(area.x, Is.EqualTo(4f));
            Assert.That(area.y, Is.EqualTo(4f));
            Assert.That(area.width, Is.EqualTo(344f));
            Assert.That(area.height, Is.EqualTo(expectedHeight));
        }

        [Test]
        public void Area_EndsBeforeTheBoardEdge()
        {
            Assert.That(HudPanelLayout.Area(720f).xMax, Is.LessThan(HudPanelLayout.BoardLeftEdge));
        }

        [Test]
        public void Padding_PlacesTheContentBetween18And338()
        {
            Rect area = HudPanelLayout.Area(720f);
            RectOffset padding = HudPanelLayout.Padding();

            Assert.That(area.x + padding.left, Is.EqualTo(18f));
            Assert.That(area.xMax - padding.right, Is.EqualTo(338f));
        }
    }

    public sealed class HudArtTests
    {
        [Test]
        public void All_ListsTheTenDistinctTextures()
        {
            Assert.That(HudArt.All, Has.Length.EqualTo(10));
            Assert.That(HudArt.All, Is.Unique);
        }

        [Test]
        public void All_LoadEveryTextureFromResources()
        {
            foreach (string path in HudArt.All)
                Assert.That(Resources.Load<Texture2D>(path), Is.Not.Null, path);
        }

        [Test]
        public void Borders_FitInsideTheirTextures()
        {
            AssertBorderFits(HudArt.Panel, HudPanelLayout.PanelBorder);
            foreach (string path in HudArt.All)
                if (path != HudArt.Panel) AssertBorderFits(path, HudPanelLayout.PlateBorder);
        }

        private static void AssertBorderFits(string path, int border)
        {
            Texture2D texture = Resources.Load<Texture2D>(path);

            Assert.That(texture, Is.Not.Null, path);
            Assert.That(texture.width, Is.GreaterThanOrEqualTo(border * 2), path);
            Assert.That(texture.height, Is.GreaterThanOrEqualTo(border * 2), path);
        }
    }
}
