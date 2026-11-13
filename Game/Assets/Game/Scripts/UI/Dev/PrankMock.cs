using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 Yaramazlıklar scenarios for the UI mock (docs/V5_YARAMAZLIKLAR.md): <c>-uimock "v5.meeting_prank*"</c>. The folder's
    /// tab (patience, prices, uses, reasons, name buttons), the G wheel, the post-it words and the plane note card, aiming,
    /// the coffee mug flying and its stain, the paper plane and its note, post-its on foreheads, a hidden phone ring, the
    /// tutanak (projector stamp, the manager's line), flattering, standing up, the cake at the results and the full reveal.
    /// "_en" variants. Seat 0 is me (Mavi); Pembe (2) and Yeşil (3) are bots, as LineMock.BaseView makes them.
    /// </summary>
    public static class PrankMockScenarios
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            foreach (var en in new[] { false, true })
            {
                string s = en ? "_en" : "";
                bool e = en;
                LineMock.Register("v5.meeting_prank_menu" + s, () => Menu(e));
                LineMock.Register("v5.meeting_prank_wheel" + s, () => Wheel(e));
                LineMock.Register("v5.meeting_prank_coffee" + s, () => Coffee(e));
                LineMock.Register("v5.meeting_prank_stain" + s, () => Stain(e));
                LineMock.Register("v5.meeting_prank_plane" + s, () => Plane(e));
                LineMock.Register("v5.meeting_prank_postit" + s, () => PostIt(e));
                LineMock.Register("v5.meeting_prank_phone" + s, () => Phone(e));
                LineMock.Register("v5.meeting_prank_tutanak" + s, () => Tutanak(e));
                LineMock.Register("v5.meeting_prank_flatter" + s, () => Flatter(e));
                LineMock.Register("v5.meeting_prank_cake" + s, () => Cake(e));
                LineMock.Register("v5.meeting_prank_lobby" + s, () => Lobby(e));
            }
        }

        static MeetingPranks UI => MeetingUi.I != null ? MeetingUi.I.Pranks : null;

        // ================================================================== views

        static readonly LineConfig Cfg = LineConfig.CreateDefault();

        static LinePranksView Pv(int session, int patience)
        {
            var pv = new LinePranksView { Session = session, Patience = patience, Level = LinePranks.Level(patience, Cfg.PatienceCapacity), Multiplier = 1 };
            for (int i = 0; i < 6; i++) { pv.PostIts.Add(-1); pv.Stains.Add(0); pv.Cakes.Add(0); }
            bool results = session == LinePranks.SessionResults;
            for (int k = 1; k <= LinePranks.KindCount; k++)
            {
                var kind = (PrankKind)k;
                int limit = Cfg.PrankPerSession(kind);
                pv.Options.Add(new LinePrankOptionView
                {
                    Kind = kind, Cost = Cfg.PrankCost(kind), Patience = kind == PrankKind.Flatter ? -Cfg.FlatterRelief : Cfg.PrankPatience(kind),
                    UsesLeft = kind == PrankKind.Cake ? Cfg.CakeMax : limit > 0 ? limit : -1,
                    Block = results ? (kind == PrankKind.Cake ? LineFailure.None : LineFailure.WrongPhase) : (kind == PrankKind.Cake ? LineFailure.WrongPhase : LineFailure.None),
                });
            }
            return pv;
        }

        static LinePrankOptionView Opt(LinePranksView pv, PrankKind k) { return pv.Options.Find(o => o.Kind == k); }

        static void Used(LinePranksView pv, PrankKind k) { var o = Opt(pv, k); o.UsesLeft = 0; o.Block = LineFailure.AlreadyUsed; }

        static LinePrankEntryView Log(LinePranksView pv, PrankKind k, int actor, int target, int word = -1, string note = "", bool tutanak = false)
        {
            var e = new LinePrankEntryView
            {
                Index = pv.Log.Count, Kind = k, Actor = actor, Target = target, Word = word, Note = note, Session = pv.Session, Phase = LinePhase.MeetingDiscussion,
                Time = LineMock.Now - 5, Cost = Cfg.PrankCost(k), Patience = k == PrankKind.Flatter ? -2 : Cfg.PrankPatience(k), Culprit = k == PrankKind.PhoneRing ? target : actor,
                Tutanak = tutanak,
            };
            pv.Log.Add(e);
            return e;
        }

        /// <summary>Meeting 1's discussion, four colleagues, the pranks open.</summary>
        static LineView Room(bool en, LinePranksView pv, int players = 4)
        {
            var v = MeetingMockScenarios.Discussion(players);
            if (en) LineMock.SetNames(v, true);
            v.Features.PranksEnabled = true;
            v.Features.PranksOpen = true;
            v.Pranks = pv;
            v.Mine.Prim = 9;
            return v;
        }

        static void Show(LineView v) { MeetingMockScenarios.Show(v); }

        static void Event(LineEventType type, int actor, int target, int a = -1, int b = -1, int c = -1)
        {
            LineClient.I.ApplyEvents(new List<LineEvent>
            {
                new LineEvent { Type = type, Actor = actor, Target = target, A = a, B = b, C = c, Audience = LineAudience.All, Time = LineMock.Now },
            });
        }

        static void Prank(PrankKind k, int actor, int target, int index, int word = -1) { Event(LineEventType.Prank, actor, target, (int)k, word, index); }

        static LineMockScenario Start(bool en)
        {
            var s = new LineMockScenario();
            if (en) s.Do(0f, MeetingMockScenarios.English);
            return s;
        }

        static float T0(bool en) { return en ? 0.3f : 0f; }

        static void Begin() { MeetingMockScenarios.Reset(); UI?.Close(); }

        /// <summary>Looks at a colleague's chest from my seat (a stain, a hit).</summary>
        static void AimChest(int slot)
        {
            // beside the colleague (the crosshair on nobody: no name tag over the shirt)
            var room = MeetingMockScenarios.Room();
            var a = room != null ? room.SeatAnchorOfSlot(slot) : null;
            if (a != null) MeetingMockScenarios.AimAt(a.position + a.right * 0.5f + Vector3.up * 1.05f);
        }

        /// <summary>Between two colleagues (a throw from one to the other in view).</summary>
        static void AimBetween(int a, int b, float down = 0.2f)
        {
            var room = MeetingMockScenarios.Room();
            Vector3 ha, hb;
            if (room != null && room.SeatHead(a, out ha) && room.SeatHead(b, out hb)) MeetingMockScenarios.AimAt((ha + hb) * 0.5f + Vector3.down * down);
        }

        // ================================================================== scenarios

        /// <summary>The folder's tab: the patience, every prank with its price / patience / uses / reason, name buttons.</summary>
        static LineMockScenario Menu(bool en)
        {
            float t = T0(en);
            return Start(en)
                .Do(t, () =>
                {
                    Begin();
                    var pv = Pv(LinePranks.SessionMeeting1, 2);
                    Log(pv, PrankKind.Slurp, 3, -1);
                    Log(pv, PrankKind.Coffee, 0, 2);
                    Used(pv, PrankKind.Coffee);
                    var slurp = Opt(pv, PrankKind.Slurp);
                    slurp.Block = LineFailure.RateLimited; slurp.ReadyAt = LineMock.Now + 7;
                    pv.Stains[2] = 1;
                    var v = Room(en, pv);
                    v.Mine.Prim = 7;
                    Show(v);
                    MeetingMockScenarios.Aim(0f, 6f);
                })
                .Do(t + 0.3f, () => { UI.Selected = PrankKind.PhoneRing; MeetingUi.I?.OpenFolder(MeetingFolder.Tab.Pranks); }, true)
                .Do(t + 1.9f, () => { UI.Selected = PrankKind.Plane; MeetingUi.I?.Folder.MarkDirty(); }, true)
                // the final, sealed (a candidate): only the free pranks; and the defence doubles the patience
                .Do(t + 3.5f, () =>
                {
                    MeetingMockScenarios.Fresh();
                    var pv = Pv(LinePranksView_Final, 4);
                    pv.Multiplier = 2;
                    foreach (var o in pv.Options)
                    {
                        if (o.Kind != PrankKind.Flatter && o.Kind != PrankKind.Cake) o.Patience *= 2;
                        if (o.Cost > 0) o.Block = LineFailure.DeskSealed;
                    }
                    var v = MeetingMockScenarios.Defense(4);
                    if (en) LineMock.SetNames(v, true);
                    v.Features.PranksEnabled = v.Features.PranksOpen = true;
                    v.Final.Candidates.Add(0);
                    foreach (var p in v.Players) if (p.Id == 0) p.Candidate = true;
                    v.Pranks = pv;
                    Show(v);
                })
                .Do(t + 3.8f, () => { UI.Selected = PrankKind.StandUp; MeetingUi.I?.OpenFolder(MeetingFolder.Tab.Pranks); }, true);
        }

        const int LinePranksView_Final = LinePranks.SessionFinal;

        /// <summary>The G wheel, a hovered slice, the post-it words, the plane note card, aiming at a colleague.</summary>
        static LineMockScenario Wheel(bool en)
        {
            float t = T0(en);
            return Start(en)
                .Do(t, () =>
                {
                    Begin();
                    var pv = Pv(LinePranks.SessionMeeting1, 3);
                    Used(pv, PrankKind.PhoneRing);
                    var stand = Opt(pv, PrankKind.StandUp);
                    stand.Block = LineFailure.RateLimited; stand.ReadyAt = LineMock.Now + 6;
                    Show(Room(en, pv));
                    MeetingMockScenarios.Aim(0f, 6f);
                    UI.OpenWheel();
                }, true)
                .Do(t + 1.4f, () => UI.MockHover(PrankKind.Coffee), true)
                .Do(t + 2.8f, () => UI.MockDialog(PrankKind.PostIt, LinePranks.WordLiar, ""), true)
                .Do(t + 4.2f, () => UI.MockDialog(PrankKind.Plane, -1, en ? "Meet me after the meeting." : "Toplantıdan sonra konuşalım."), true)
                .Do(t + 5.6f, () => { UI.Close(); UI.Arm(PrankKind.Coffee); MeetingMockScenarios.AimAtSlot(1); }, true);
        }

        /// <summary>Pembe throws coffee at Yeşil (in flight, the splash, the stain and Yeşil's line); then at me.</summary>
        static LineMockScenario Coffee(bool en)
        {
            float t = T0(en);
            LinePranksView pv = null;
            return Start(en)
                .Do(t, () =>
                {
                    Begin();
                    pv = Pv(LinePranks.SessionMeeting1, 0);
                    Show(Room(en, pv));
                    AimBetween(1, 3, 0.25f);
                })
                .Do(t + 0.8f, () =>
                {
                    Log(pv, PrankKind.Coffee, 1, 3);
                    pv.Patience = 3; pv.Level = 1; pv.Stains[3] = 1;
                    Show(Room(en, pv));
                    Prank(PrankKind.Coffee, 1, 3, 0);
                    Event(LineEventType.Patience, 1, 1, 3, 1, 0);
                }, 0.32f)
                .Do(t + 1.6f, () => { }, 0.05f)
                .Do(t + 2.2f, () => AimChest(3), 0.6f)
                .Do(t + 3.0f, () => BotSpeech.Shout(3, "bot.prank.coffee.h", null), 0.9f)
                .Do(t + 4.6f, () =>
                {
                    // and now at me: the splash over my screen
                    Log(pv, PrankKind.Coffee, 1, 0);
                    pv.Patience = 0; pv.Level = 0; pv.Stains[0] = 1;
                    Show(Room(en, pv));
                    MeetingMockScenarios.AimAtSlot(1);
                    Prank(PrankKind.Coffee, 1, 0, 1);
                }, 0.85f);
        }

        /// <summary>Stains stay for the whole match (later: the crisis vote): two colleagues, closer.</summary>
        static LineMockScenario Stain(bool en)
        {
            float t = T0(en);
            return Start(en)
                .Do(t, () =>
                {
                    Begin();
                    var pv = Pv(LinePranks.SessionMeeting2, 1);
                    pv.Stains[1] = 2; pv.Stains[3] = 1;
                    var v = Room(en, pv);
                    v.Day = 1;
                    Show(v);
                    MeetingMockScenarios.Aim(0f, 6f);
                }, 1.6f)
                .Do(t + 2.2f, () => AimChest(1), 1.2f)
                .Do(t + 3.8f, () => AimChest(3), 1.2f);
        }

        /// <summary>A paper plane from Sarı to Yeşil, then one to me: the toast, the prompt on the plane, the note.</summary>
        static LineMockScenario Plane(bool en)
        {
            float t = T0(en);
            LinePranksView pv = null;
            return Start(en)
                .Do(t, () => { Begin(); pv = Pv(LinePranks.SessionMeeting1, 1); Show(Room(en, pv)); AimBetween(1, 3, 0.35f); })
                .Do(t + 0.8f, () =>
                {
                    Log(pv, PrankKind.Plane, 1, 3, note: "x");
                    pv.Patience = 3; pv.Level = 1;
                    Show(Room(en, pv));
                    Prank(PrankKind.Plane, 1, 3, 0);
                }, 0.55f)
                .Do(t + 2.6f, () => BotSpeech.Shout(3, "bot.prank.plane.h", null), 0.8f)
                .Do(t + 4.0f, () =>
                {
                    var bot = new BotLine { Key = "bot.prank.note.m1" };
                    Log(pv, PrankKind.Plane, 2, 0, note: LineBotLines.Token(bot));
                    pv.Patience = 5; pv.Level = 2;
                    Show(Room(en, pv));
                    MeetingMockScenarios.Aim(0f, 30f);
                    Prank(PrankKind.Plane, 2, 0, 1);
                }, 1.6f)
                .Do(t + 6.2f, () =>
                {
                    var room = MeetingMockScenarios.Room();
                    var a = room != null ? room.SeatAnchorOfSlot(0) : null;
                    if (a == null) return;
                    // look at the plane on my table (the prompt)
                    Vector3 toMiddle = room.transform.TransformPoint(new Vector3(0f, 0f, -0.15f)) - a.position;
                    float middle = Vector3.Dot(a.right, toMiddle) >= 0f ? 1f : -1f;
                    MeetingMockScenarios.AimAt(a.position + a.forward * 0.55f + a.right * middle * 0.36f + Vector3.up * 0.77f);
                }, 0.8f)
                .Do(t + 7.6f, () => UI.OpenNote(-1), 0.6f);
        }

        /// <summary>Post-its on two foreheads (readable), and one on mine.</summary>
        static LineMockScenario PostIt(bool en)
        {
            float t = T0(en);
            LinePranksView pv = null;
            return Start(en)
                .Do(t, () => { Begin(); pv = Pv(LinePranks.SessionMeeting1, 0); pv.PostIts[1] = LinePranks.WordThief; Show(Room(en, pv)); MeetingMockScenarios.AimAtSlot(3); })
                .Do(t + 0.8f, () =>
                {
                    Log(pv, PrankKind.PostIt, 1, 3, LinePranks.WordLiar);
                    pv.PostIts[3] = LinePranks.WordLiar; pv.Patience = 2; pv.Level = 1;
                    Show(Room(en, pv));
                    Prank(PrankKind.PostIt, 1, 3, 0, LinePranks.WordLiar);
                }, 0.25f)
                .Do(t + 2.0f, () => BotSpeech.Shout(3, "bot.prank.postit.h", null), 0.9f)
                .Do(t + 3.4f, () => MeetingMockScenarios.AimAtSlot(1), 1.0f)
                .Do(t + 4.8f, () =>
                {
                    Log(pv, PrankKind.PostIt, 2, 0, LinePranks.WordRich);
                    pv.PostIts[0] = LinePranks.WordRich; pv.Patience = 4; pv.Level = 2;
                    Show(Room(en, pv));
                    MeetingMockScenarios.Aim(0f, 6f);
                    Prank(PrankKind.PostIt, 2, 0, 1, LinePranks.WordRich);
                }, 1.2f);
        }

        /// <summary>A hidden phone ring at Pembe: panic, answering, the manager's line; Pembe's denial.</summary>
        static LineMockScenario Phone(bool en)
        {
            float t = T0(en);
            LinePranksView pv = null;
            return Start(en)
                .Do(t, () => { Begin(); pv = Pv(LinePranks.SessionMeeting1, 1); Show(Room(en, pv)); MeetingMockScenarios.AimAtSlot(3); })
                .Do(t + 0.8f, () =>
                {
                    Log(pv, PrankKind.PhoneRing, -1, 3);
                    pv.Patience = 4; pv.Level = 2;
                    Show(Room(en, pv));
                    Prank(PrankKind.PhoneRing, -1, 3, 0);
                    Event(LineEventType.Patience, -1, 3, 4, 2, 0);
                }, 0.7f)
                .Do(t + 2.4f, () => { }, 0.4f)
                .Do(t + 3.4f, () => BotSpeech.Shout(3, "bot.prank.phone.h", null), 0.8f)
                .Do(t + 4.8f, () => { var room = MeetingMockScenarios.Room(); if (room != null && room.Manager != null) MeetingMockScenarios.AimAt(room.Manager.transform.position + Vector3.up * 1.3f); }, 0.8f);
        }

        /// <summary>Red patience; Sarı's coffee overflows it: the manager points, the TUTANAK stamp on the projector, the board.</summary>
        static LineMockScenario Tutanak(bool en)
        {
            float t = T0(en);
            LinePranksView pv = null;
            return Start(en)
                .Do(t, () =>
                {
                    Begin();
                    pv = Pv(LinePranks.SessionMeeting1, 5);
                    Log(pv, PrankKind.Slurp, 3, -1); Log(pv, PrankKind.PostIt, 2, 1, LinePranks.WordQuiet); Log(pv, PrankKind.Plane, 1, 3, note: "x");
                    pv.PostIts[1] = LinePranks.WordQuiet;
                    Show(Room(en, pv));
                    MeetingMockScenarios.Aim(0f, 6f);
                    MeetingUi.I?.OpenProjector();
                }, true)
                .Do(t + 1.4f, () =>
                {
                    MeetingUi.I?.CloseAll();
                    Log(pv, PrankKind.Coffee, 1, 3, tutanak: true).Culprit = 1;
                    pv.Patience = 0; pv.Level = 0; pv.Stains[3] = 1;
                    var v = Room(en, pv);
                    foreach (var p in v.Players) if (p.Id == 1) p.Blame += 1;
                    Show(v);
                    var room = MeetingMockScenarios.Room();
                    if (room != null && room.Manager != null) MeetingMockScenarios.AimAt(room.Manager.transform.position + Vector3.up * 1.25f + Vector3.right * 0.01f);
                    Prank(PrankKind.Coffee, 1, 3, 3);
                    Event(LineEventType.Tutanak, 1, 1, (int)PrankKind.Coffee, 1);
                    Event(LineEventType.BlameAdded, -1, 1, 1, (int)BlameReason.Tutanak);
                    Event(LineEventType.Patience, 1, 1, 0, 0, 2);
                }, 1.6f)
                .Do(t + 3.4f, () => MeetingUi.I?.OpenProjector(), 0.45f)
                .Do(t + 4.8f, () => { MeetingUi.I?.CloseAll(); MeetingUi.I?.OpenBoard(); }, 1.0f);
        }

        /// <summary>Flattering the manager (pleased, points at Sarı), standing up to slam the table, a slurp.</summary>
        static LineMockScenario Flatter(bool en)
        {
            float t = T0(en);
            LinePranksView pv = null;
            return Start(en)
                .Do(t, () =>
                {
                    Begin();
                    pv = Pv(LinePranks.SessionMeeting1, 4);
                    Show(Room(en, pv));
                    var room = MeetingMockScenarios.Room();
                    if (room != null && room.Manager != null) MeetingMockScenarios.AimAt(room.Manager.transform.position + Vector3.up * 1.2f);
                })
                .Do(t + 0.8f, () =>
                {
                    Log(pv, PrankKind.Flatter, 1, -1);
                    pv.Patience = 2; pv.Level = 1;
                    Show(Room(en, pv));
                    Prank(PrankKind.Flatter, 1, -1, 0);
                    Event(LineEventType.Patience, 1, -1, 2, 1, 2);
                }, 1.0f)
                .Do(t + 2.2f, () => { }, 0.6f)
                // Yeşil stands up, slams the table and points at Pembe ("İtiraz ediyorum!")
                .Do(t + 4.0f, () =>
                {
                    Log(pv, PrankKind.StandUp, 3, 1);
                    pv.Patience = 3;
                    Show(Room(en, pv));
                    AimBetween(1, 3, 0.05f);
                    Prank(PrankKind.StandUp, 3, 1, 1);
                }, 1.1f)
                .Do(t + 6.4f, () =>
                {
                    Log(pv, PrankKind.Slurp, 1, -1);
                    pv.Patience = 4; pv.Level = 2;
                    Show(Room(en, pv));
                    MeetingMockScenarios.AimAtSlot(1);
                    Prank(PrankKind.Slurp, 1, -1, 2);
                    Event(LineEventType.Patience, 1, 1, 4, 2, 1);
                }, 0.7f);
        }

        /// <summary>The results: a cake from Pembe into the winner's face, one into mine, the reveal tab with every prank.</summary>
        static LineMockScenario Cake(bool en)
        {
            float t = T0(en);
            LinePranksView pv = null;
            return Start(en)
                .Do(t, () =>
                {
                    Begin();
                    MeetingUi.NoAutoCamera = true;
                    pv = Pv(LinePranks.SessionResults, 0);
                    Show(Results(en, pv));
                    AimBetween(2, 1, 0.1f);
                })
                .Do(t + 0.8f, () =>
                {
                    Log(pv, PrankKind.Cake, 2, 1).Phase = LinePhase.Results;
                    pv.Cakes[1] = 1;
                    Show(Results(en, pv));
                    Prank(PrankKind.Cake, 2, 1, pv.Log.Count - 1);
                }, 0.3f)
                .Do(t + 2.0f, () => { MeetingMockScenarios.AimAtSlot(1); BotSpeech.Shout(1, "bot.prank.cake.h", null); }, 1.0f)
                .Do(t + 3.6f, () =>
                {
                    Log(pv, PrankKind.Cake, 3, 0).Phase = LinePhase.Results;
                    pv.Cakes[0] = 1;
                    Show(Results(en, pv));
                    MeetingMockScenarios.AimAtSlot(3);
                    Prank(PrankKind.Cake, 3, 0, pv.Log.Count - 1);
                }, 0.9f)
                .Do(t + 5.6f, () => { MeetingSlide.MockResultsTab = MeetingReveal.TabPranks; }, 1.4f)
                .Do(t + 7.4f, () => { MeetingUi.NoAutoCamera = false; });
        }

        /// <summary>The lobby's "Yaramazlıklar açık" checkbox (host controls), on and off; the how-to page.</summary>
        static LineMockScenario Lobby(bool en)
        {
            float t = T0(en);
            return Start(en)
                .Do(t, () => { MenuScreensMock(true); }, true)
                .Do(t + 1.4f, () => { MenuScreensMock(false); }, true)
                .Do(t + 2.8f, () => { LobbyScreen.MockHost = false; HowToPlayScreen.OpenAt(HowToPlayScreen.PagePranks); }, true);
        }

        static void MenuScreensMock(bool pranks)
        {
            LobbyScreen.MockHost = true;
            LineClient.I.ClearWeek();
            var lobby = new LineLobbyView { RecordingsEnabled = true, PranksEnabled = pranks, StatusKey = "lobby.need_ready", SessionId = "mock" };
            for (int i = 0; i < 6; i++)
                lobby.Slots.Add(new LineLobbySlot { Slot = i, Occupied = i < 4, Connected = i < 4, Ready = i == 0 || i == 2, IsHost = i == 0, IsBot = i >= 2 && i < 4,
                                                    Name = i < 4 ? LineMock.NamesTr[i] : "" });
            LineClient.I.ApplyLobby(lobby);
        }

        static LineView Results(bool en, LinePranksView pv)
        {
            var v = LineMock.BaseView(LinePhase.Results, 4, 2, 40);
            if (en) LineMock.SetNames(v, true);
            var r = new LineWeekResult { WeekId = 1, MatchId = "mock", PlayerCount = 4, ResultHash = 7654321 };
            int[] prim = { 11, 16, 0, 9 };
            int[] blame = { 3, 5, 9, 4 };
            for (int i = 0; i < 4; i++)
                r.Players.Add(new LineWeekPlayerResult { Player = i, Prim = prim[i], Blame = blame[i], Fired = i == 2, Winner = i == 1, WasCandidate = i == 2 || i == 1 });
            r.Winners.Add(1);
            r.Fired.Add(2);
            r.PrimLog.Add(new LinePrimChange { Player = 2, Delta = -2, Reason = PrimReason.Prank, Other = 3 });
            r.PrimLog.Add(new LinePrimChange { Player = 1, Delta = -1, Reason = PrimReason.Prank, Other = -1 });
            v.Results = r;
            v.Features.PranksEnabled = true;
            v.Features.PranksOpen = true;
            // the match's pranks, every actor revealed (the phone ring too); only my own plane note
            if (pv.Log.Count == 0)
            {
                var m1 = new LinePrankEntryView { Index = 0, Kind = PrankKind.Coffee, Actor = 2, Target = 3, Session = 0, Phase = LinePhase.MeetingDiscussion, Cost = 2, Patience = 3, Culprit = 2 };
                var m2 = new LinePrankEntryView { Index = 1, Kind = PrankKind.Plane, Actor = 3, Target = 0, Session = 0, Phase = LinePhase.MeetingDiscussion, Cost = 1, Patience = 2, Culprit = 3,
                                                  Note = en ? "I know what you did." : "Ne yaptığını biliyorum." };
                var m3 = new LinePrankEntryView { Index = 2, Kind = PrankKind.PhoneRing, Actor = 2, Target = 1, Session = 3, Phase = LinePhase.FinalDiscussion, Cost = 2, Patience = 3, Culprit = 1, Tutanak = true };
                var m4 = new LinePrankEntryView { Index = 3, Kind = PrankKind.Flatter, Actor = 1, Target = -1, Session = 3, Phase = LinePhase.FinalDefense, Cost = 1, Patience = -2, Culprit = -1 };
                pv.Log.AddRange(new[] { m1, m2, m3, m4 });
            }
            pv.Stains[3] = 1;
            v.Pranks = pv;
            return v;
        }
    }
}
