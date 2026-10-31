using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    // =================================================================================================================
    // Bank: balance, block income filter, transfers, history
    // =================================================================================================================

    public sealed class DeskBankApp : DeskAppView
    {
        public DeskBankApp(DeskUi ui) : base(ui, "dk-app-bank") { Build(); }
        public override DeskApp Id => DeskApp.Bank;
        public override string Icon => "bank_app";

        Label _balance, _incomeText, _incomeRule, _status, _amountLabel, _rule;
        VisualElement _incomeFill, _markFull, _pickHost, _history;
        Button _send, _minus, _plus;
        Action<int> _pickUpdate;
        int _target = LineIds.None, _amount = 1;
        int _sig = int.MinValue, _lastWait = -1;
        string _err = "";

        public override string Subtitle(LineView v) { return Dk.F("desk.bank.balance", v.Mine.Money); }

        void Build()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-bank-scroll").AddTo(Root);
            var c = scroll.contentContainer;
            var top = Box("dk-bank-top").AddTo(c);
            var bal = Box("dk-bank-balance").NoPick().AddTo(top);
            StampText(Loc.Upper(Dk.T("desk.bank.title")), "dk-bank-band").NoPick().AddTo(bal);
            _balance = MemoBold("", "dk-bank-amount").NoPick().AddTo(bal);
            var inc = Box("dk-bank-income").NoPick().AddTo(top);
            Bold(Dk.T("desk.bank.income_title"), "dk-bank-income-title").NoPick().AddTo(inc);
            var bar = Box("dk-incbar").NoPick().AddTo(inc);
            _incomeFill = Box("dk-incbar-fill").NoPick().AddTo(bar);
            _markFull = Box("dk-incbar-mark").NoPick().AddTo(bar);
            _incomeText = Bold("", "dk-bank-income-text").NoPick().AddTo(inc);
            _incomeRule = Text("", "dk-bank-income-rule").NoPick().AddTo(inc);

            Dk.Heading(c, Dk.T("desk.bank.transfer"), Dk.T("desk.bank.transfer_hint"));
            _pickHost = Box("dk-bank-pick").AddTo(c);
            var amt = Box("dk-amount").AddTo(c);
            Bold(Dk.T("desk.bank.amount"), "dk-amount-label").NoPick().AddTo(amt);
            _minus = Btn("-", () => Step(-1), "btn-secondary", "dk-amount-btn").AddTo(amt);
            _amountLabel = MemoBold("1", "dk-amount-value").NoPick().AddTo(amt);
            _plus = Btn("+", () => Step(1), "btn-secondary", "dk-amount-btn").AddTo(amt);
            Box("spacer").NoPick().AddTo(amt);
            _send = Btn(Dk.T("desk.bank.send"), Send, "btn-primary", "dk-composer-send").AddTo(amt);
            _status = Text("", "dk-composer-status").NoPick().AddTo(c);
            _rule = Text("", "dk-bank-rule").NoPick().AddTo(c);

            Dk.Heading(c, Dk.T("desk.bank.history"));
            _history = Box("dk-bank-history").AddTo(c);
        }

        internal void MockFill(int target, int amount) { _target = target; _amount = amount; _sig = int.MinValue; var v = Dk.View; if (v != null) Refresh(v, true); }

        void Step(int d)
        {
            var v = Dk.View;
            if (v == null) return;
            _amount = Mathf.Clamp(_amount + d, 1, MaxAmount(v));
            _err = "";
            Update(v);
        }

        static int MaxAmount(LineView v) { return Mathf.Max(1, Mathf.Min(Dk.Config.TransferMax, v.Mine.Money)); }

        public override void OnShown() { _sig = int.MinValue; var v = Dk.View; if (v != null) Refresh(v, true); }

        public override void Refresh(LineView v, bool force)
        {
            int sig = v.Players.Count * 31 + v.Mine.Money * 7 + v.Mine.BlockIncome * 13 + v.Mine.Records.Count;
            for (int i = 0; i < v.Players.Count; i++) sig = sig * 3 + (v.Players[i].Connected ? 1 : 0);
            if (force || sig != _sig)
            {
                bool rebuildPick = force || _sig == int.MinValue || (_sig / 31) != (sig / 31);
                _sig = sig;
                _pickHost.Clear();
                _pickUpdate = Dk.Picker(_pickHost, v, id => { _target = id; _err = ""; _pickUpdate(id); Update(Dk.View); }, false, "badge-m");
                BuildHistory(v);
            }
            Update(v);
        }

        void Update(LineView v)
        {
            var cfg = Dk.Config;
            _amount = Mathf.Clamp(_amount, 1, MaxAmount(v));
            _pickUpdate?.Invoke(_target);
            _balance.SetText(Dk.F("desk.bank.balance", v.Mine.Money));
            int full = cfg.BlockIncomeFull, cap = cfg.BlockIncomeCap;
            _incomeFill.style.width = Length.Percent(Mathf.Clamp01(v.Mine.BlockIncome / (float)Mathf.Max(1, cap)) * 100f);
            _markFull.style.left = Length.Percent(Mathf.Clamp01(full / (float)Mathf.Max(1, cap)) * 100f);
            _incomeText.SetText(Dk.F("desk.bank.income_now", v.Mine.BlockIncome, cap));
            _incomeRule.SetText(v.Mine.BlockIncome >= cap ? Dk.F("desk.bank.income_capped", cap)
                : v.Mine.BlockIncome >= full ? Dk.F("desk.bank.income_half", full, cap)
                : Dk.F("desk.bank.income_rule", full, cap));
            _incomeFill.EnableInClassList("dk-incbar-fill-half", v.Mine.BlockIncome >= full);
            _incomeFill.EnableInClassList("dk-incbar-fill-cap", v.Mine.BlockIncome >= cap);
            _amountLabel.SetText(Num(_amount));
            _minus.SetEnabled(_amount > 1);
            _plus.SetEnabled(_amount < MaxAmount(v));
            _rule.SetText(Dk.F("desk.bank.rule", cfg.TransferMax, cfg.TransferMinInterval.ToString("0.#", Loc.Culture)));
            string why = Why(v);
            _send.SetEnabled(why.Length == 0);
            _status.SetText(_err.Length > 0 ? _err : why.Length > 0 ? why : Dk.F("desk.bank.ready", _amount, _target >= 0 ? Dk.Name(_target) : "?"));
            _status.EnableInClassList("dk-composer-status-bad", _err.Length > 0);
            Dk.NoRich(_status);
        }

        string Why(LineView v)
        {
            if (!v.Features.TransfersOpen) return Dk.T("desk.bank.closed");
            if (Dk.ComputerBlocked(v)) return Dk.T("desk.msg.blocked");
            if (v.Mine.Money <= 0) return Dk.T("desk.bank.broke");
            if (_target < 0) return Dk.T("desk.bank.pick");
            double wait = v.Mine.NextTransferAt - Dk.Now;
            if (wait > 0.05) return Dk.F("desk.msg.rate", Mathf.CeilToInt((float)wait));
            return "";
        }

        public override void Tick(LineView v)
        {
            int w = Mathf.CeilToInt((float)Math.Max(0, v.Mine.NextTransferAt - Dk.Now));
            if (w != _lastWait) { _lastWait = w; Update(v); }
        }

        void Send()
        {
            var v = Dk.View;
            if (v == null || Why(v).Length > 0) { Dk.Play("deny"); return; }
            _err = "";
            Dk.Send(LineVerb.Transfer, _target, _amount);
        }

        public override bool OnResult(LineActionResult r, LineAction req)
        {
            if (req.Verb != LineVerb.Transfer) return false;
            if (r.Accepted) { Dk.Play("coin", "toast"); _err = ""; return false; }
            _err = Dk.T("desk.fail." + r.Failure);
            var v = Dk.View;
            if (v != null) Update(v);
            return true;
        }

        void BuildHistory(LineView v)
        {
            _history.Clear();
            int shown = 0;
            var rs = v.Mine.Records;
            for (int i = rs.Count - 1; i >= 0 && shown < 24; i--)
            {
                var r = rs[i];
                if (r.Kind != RecordKind.Transfer && r.Kind != RecordKind.DealPayment && r.Kind != RecordKind.OfferAcceptance) continue;
                shown++;
                bool outgoing = r.Kind == RecordKind.Transfer && r.Actor == v.Me;
                bool incoming = r.Kind == RecordKind.Transfer && r.Subject == v.Me;
                var row = Box("dk-hist", outgoing ? "dk-hist-out" : "dk-hist-in").AddTo(_history);
                Dk.Icon("bank_app", "dk-hist-icon").NoPick().AddTo(row);
                var col = Box("dk-hist-col").NoPick().AddTo(row);
                Bold(r.Kind == RecordKind.Transfer ? (outgoing ? Dk.F("desk.bank.h_out", Dk.Name(r.Subject)) : incoming ? Dk.F("desk.bank.h_in", Dk.Name(r.Actor)) : Dk.F("desk.bank.h_other", Dk.Name(r.Actor), Dk.Name(r.Subject))) : Dk.RecordKindName(r.Kind), "dk-hist-name").Plain().NoPick().AddTo(col);
                Text(Dk.Ago(r.Time), "dk-hist-time").NoPick().AddTo(col);
                Box("spacer").NoPick().AddTo(row);
                int amount = r.Kind == RecordKind.Transfer ? r.N1 : r.N1;
                MemoBold((outgoing ? "-" : "+") + Mathf.Max(0, amount), "dk-hist-amount").NoPick().AddTo(row);
            }
            if (shown == 0) Text(Dk.T("desk.bank.no_history"), "dk-empty").AddTo(_history);
            Dk.NoRich(_history);
        }
    }

    // =================================================================================================================
    // Evidence: my records and the offers shop
    // =================================================================================================================

    public sealed class DeskEvidenceApp : DeskAppView
    {
        public DeskEvidenceApp(DeskUi ui) : base(ui, "dk-app-evidence") { Build(); }
        public override DeskApp Id => DeskApp.Evidence;
        public override string Icon => "record_app";

        VisualElement _recs, _offers, _recsWrap, _offersWrap;
        Label _counters, _status;
        TabBar _tabs;
        int _tab;
        int _sig = int.MinValue;
        string _err = "";

        public override string Subtitle(LineView v) { return Dk.F("desk.evidence.sub", v.Mine.Records.Count); }

        void Build()
        {
            _tabs = new TabBar(Root, id => { _tab = id; ApplyTab(); }, "dk-ev-tabs");
            _tabs.Add(0, Dk.T("desk.evidence.tab_records"), "record_app");
            _tabs.Add(1, Dk.T("desk.evidence.tab_offers"), "bank_app");
            var scrollR = new ScrollView(ScrollViewMode.Vertical).Cls("dk-ev-scroll").AddTo(Root);
            _recsWrap = scrollR;
            _recs = scrollR.contentContainer;
            var scrollO = new ScrollView(ScrollViewMode.Vertical).Cls("dk-ev-scroll").AddTo(Root);
            _offersWrap = scrollO;
            _offers = scrollO.contentContainer;
            _tabs.Select(0, false);
            ApplyTab();
        }

        internal void MockTab(int t) { _tabs.Select(t); }

        void ApplyTab()
        {
            _recsWrap.Show(_tab == 0);
            _offersWrap.Show(_tab == 1);
        }

        public override void OnShown() { _sig = int.MinValue; var v = Dk.View; if (v != null) Refresh(v, true); }

        public override void Refresh(LineView v, bool force)
        {
            int sig = v.Mine.Records.Count * 31 + v.Mine.Offers.Count + v.Mine.Money * 7 + v.Mine.PurchasesLeftThisBlock * 3 + v.Mine.PurchasesLeftThisMatch * 5;
            for (int i = 0; i < v.Mine.Offers.Count; i++) sig = sig * 3 + (v.Mine.Offers[i].Purchased ? 1 : 0) + (v.Mine.Offers[i].Available ? 2 : 0) + v.Mine.Offers[i].Price;
            for (int i = 0; i < v.Mine.Records.Count; i++) if (v.Mine.Records[i].Presented) sig += i + 1;
            sig = sig * 31 + (v.Features.EvidenceOpen ? 1 : 0) + (Dk.ComputerBlocked(v) ? 2 : 0);
            if (!force && sig == _sig) return;
            _sig = sig;
            BuildRecords(v);
            BuildOffers(v);
        }

        void BuildRecords(LineView v)
        {
            _recs.Clear();
            Dk.Heading(_recs, Dk.T("desk.evidence.records"), Dk.T("desk.evidence.records_hint"));
            var rs = v.Mine.Records;
            if (rs.Count == 0) Text(Dk.T("desk.evidence.no_records"), "dk-empty").AddTo(_recs);
            var grid = Box("dk-rec-grid").AddTo(_recs);
            for (int i = rs.Count - 1; i >= 0; i--) Dk.RecordCard(rs[i], null, "dk-rec-tile").AddTo(grid);
            Text(Dk.T("desk.evidence.present_hint"), "dk-evidence-foot").NoPick().AddTo(_recs);
        }

        void BuildOffers(LineView v)
        {
            _offers.Clear();
            var cfg = Dk.Config;
            Dk.Heading(_offers, Dk.T("desk.evidence.offers"), Dk.T("desk.evidence.offers_hint"));
            var info = Box("dk-ev-info").NoPick().AddTo(_offers);
            Bold(Dk.F("desk.evidence.money", v.Mine.Money), "dk-ev-info-chip").NoPick().AddTo(info);
            Bold(Dk.F("desk.evidence.left", v.Mine.PurchasesLeftThisBlock, v.Mine.PurchasesLeftThisMatch), "dk-ev-info-chip").NoPick().AddTo(info);
            Text(Dk.F("desk.evidence.price_rule", cfg.PriceStep), "dk-ev-info-rule").NoPick().AddTo(info);
            _status = Text(_err, "dk-composer-status", "dk-composer-status-bad").NoPick().AddTo(_offers);
            _status.Show(_err.Length > 0);
            if (v.Mine.Offers.Count == 0) Text(Dk.T("desk.evidence.no_offers"), "dk-empty").AddTo(_offers);
            for (int i = 0; i < v.Mine.Offers.Count; i++)
            {
                var o = v.Mine.Offers[i];
                var row = Box("dk-offer").AddTo(_offers);
                if (o.Purchased) row.AddToClassList("dk-offer-bought");
                Dk.Icon(OfferIcon(o.Kind), "dk-offer-icon").NoPick().AddTo(row);
                var col = Box("dk-offer-col").NoPick().AddTo(row);
                Bold(Dk.OfferName(o), "dk-offer-name").NoPick().AddTo(col);
                Text(Dk.T("desk.offer.desc." + o.Kind), "dk-offer-desc").NoPick().AddTo(col);
                Box("spacer").NoPick().AddTo(row);
                MemoBold(Dk.F("desk.evidence.price", o.Price), "dk-offer-price").NoPick().AddTo(row);
                string why = BuyBlocker(v, o);
                if (o.Purchased) Tag(Dk.T("desk.evidence.bought"), "tag-green").AddTo(row);
                else
                {
                    var id = o.Id;
                    var b = Btn(Dk.T("desk.evidence.buy"), () => Buy(id), why.Length == 0 ? "btn-primary" : "btn-secondary", "btn-small").AddTo(row);
                    b.SetEnabled(why.Length == 0);
                    if (why.Length > 0) Text(why, "dk-offer-why").NoPick().AddTo(col);
                }
            }
            Dk.NoRich(_offers);
        }

        static string OfferIcon(OfferKind k) { return k == OfferKind.BankLedger ? "bank_app" : k == OfferKind.TaskAudit ? "task_app" : k == OfferKind.ITLog ? "trick_app" : "lock"; }

        string BuyBlocker(LineView v, LineOfferView o)
        {
            if (!v.Features.EvidenceOpen) return Dk.T("desk.evidence.closed");
            if (!o.Available) return Dk.T("desk.evidence.unavailable");
            if (v.Mine.PurchasesLeftThisBlock <= 0) return Dk.T("desk.evidence.no_block_left");
            if (v.Mine.PurchasesLeftThisMatch <= 0) return Dk.T("desk.evidence.no_match_left");
            if (v.Mine.Money < o.Price) return Dk.T("desk.evidence.too_poor");
            if (Dk.ComputerBlocked(v)) return Dk.T("desk.msg.blocked");
            return "";
        }

        void Buy(int offerId)
        {
            _err = "";
            Dk.Send(LineVerb.BuyEvidence, a: offerId);
        }

        public override bool OnResult(LineActionResult r, LineAction req)
        {
            if (req.Verb != LineVerb.BuyEvidence) return false;
            if (r.Accepted) { _err = ""; return false; }
            _err = Dk.T("desk.fail." + r.Failure);
            _sig = int.MinValue;
            var v = Dk.View;
            if (v != null) Refresh(v, true);
            return true;
        }
    }

    // =================================================================================================================
    // Remedial work: bounded recovery with a colleague who took part
    // =================================================================================================================

    public sealed class DeskRemedialApp : DeskAppView
    {
        public DeskRemedialApp(DeskUi ui) : base(ui, "dk-app-remedial") { Build(); }
        public override DeskApp Id => DeskApp.Remedial;
        public override string Icon => "remedial";

        VisualElement _body;
        int _task = -1, _cosigner = LineIds.None;
        int _sig = int.MinValue, _lastSec = -1;
        Label _timerIn, _timerOut;
        string _err = "";

        public override string Subtitle(LineView v) { return Dk.F("desk.remedial.left", v.Mine.RemedialsLeftThisBlock, v.Mine.RemedialsLeftThisMatch); }

        void Build()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-rem-scroll").AddTo(Root);
            _body = scroll.contentContainer;
        }

        public override int Badge(LineView v) { return v.Mine.IncomingRemedial != null ? -1 : 0; }

        internal void MockPick(int task, int who) { _task = task; _cosigner = who; _sig = int.MinValue; var v = Dk.View; if (v != null) Refresh(v, true); }

        public override void OnShown() { _sig = int.MinValue; var v = Dk.View; if (v != null) Refresh(v, true); }

        public override void Refresh(LineView v, bool force)
        {
            var m = v.Mine;
            int sig = m.RemedialOptions.Count * 31 + (m.IncomingRemedial != null ? m.IncomingRemedial.Requester + 10 : 0) + (m.OutgoingRemedial != null ? m.OutgoingRemedial.Cosigner + 20 : 0);
            sig = sig * 31 + m.RemedialsLeftThisBlock * 3 + m.RemedialsLeftThisMatch + (v.Features.RemedialOpen ? 100 : 0) + _task * 7 + _cosigner * 13;
            for (int i = 0; i < m.RemedialOptions.Count; i++) sig = sig * 3 + m.RemedialOptions[i];
            sig = sig * 31 + (Dk.ComputerBlocked(v) ? 1 : 0) + _err.Length;
            if (!force && sig == _sig) return;
            _sig = sig;
            Build(v);
        }

        string TaskLabel(LineView v, int number, out int output, out int max)
        {
            output = -1; max = -1;
            for (int i = 0; i < v.TodayReveals.Count; i++)
                if (v.TodayReveals[i].Number == number) { output = v.TodayReveals[i].Output; max = v.TodayReveals[i].MaxOutput; return Dk.TaskName(v.TodayReveals[i].Kind); }
            var rs = v.Mine.Records;
            for (int i = rs.Count - 1; i >= 0; i--)
                if (rs[i].Kind == RecordKind.TaskOutcome && rs[i].TaskNumber == number) { output = rs[i].N1; max = rs[i].N2; return Dk.TaskName(rs[i].Task); }
            return Dk.F("desk.remedial.task_n", number + 1);
        }

        void Build(LineView v)
        {
            var m = v.Mine;
            var cfg = Dk.Config;
            _body.Clear();
            _timerIn = _timerOut = null;
            _lastSec = -1;
            // explanation
            var info = Box("dk-rem-info").NoPick().AddTo(_body);
            Dk.Icon("remedial", "dk-rem-info-icon").AddTo(info);
            var ic = Box("dk-rem-info-col").NoPick().AddTo(info);
            Title(Dk.T("desk.remedial.title"), "dk-rem-title").NoPick().AddTo(ic);
            Text(Dk.F("desk.remedial.effect", cfg.RemedialOutput, cfg.RemedialBlame, cfg.RemedialIncome), "dk-rem-text").NoPick().AddTo(ic);
            Text(Dk.F("desk.remedial.caps", cfg.RemedialsPerBlock, cfg.RemedialsPerMatch, Mathf.RoundToInt(cfg.Durations.RemedialTimeout)), "dk-rem-text", "dk-rem-caps").NoPick().AddTo(ic);

            if (!v.Features.RemedialOpen)
            {
                Text(Dk.T("desk.remedial.closed"), "dk-empty").AddTo(_body);
                return;
            }

            // incoming request
            if (m.IncomingRemedial != null)
            {
                var r = m.IncomingRemedial;
                var card = Box("dk-rem-card", "dk-rem-incoming").AddTo(_body);
                Dk.Badge(r.Requester, "badge-m").AddTo(card);
                var col = Box("dk-rem-col").NoPick().AddTo(card);
                Bold(Dk.F("desk.remedial.incoming", Dk.Name(r.Requester)), "dk-rem-card-title").Plain().NoPick().AddTo(col);
                Text(Dk.F("desk.remedial.incoming_sub", Dk.TaskName(r.Task)), "dk-rem-card-sub").Plain().NoPick().AddTo(col);
                _timerIn = MemoBold("", "dk-rem-timer").NoPick().AddTo(card);
                Btn(Dk.T("desk.remedial.decline"), () => Dk.Send(LineVerb.RespondRemedial, r.Requester, 0), "btn-ghost").AddTo(card);
                Btn(Dk.T("desk.remedial.accept"), () => Dk.Send(LineVerb.RespondRemedial, r.Requester, 1), "btn-primary").AddTo(card);
            }

            // outgoing request
            if (m.OutgoingRemedial != null)
            {
                var r = m.OutgoingRemedial;
                var card = Box("dk-rem-card", "dk-rem-outgoing").AddTo(_body);
                Dk.Badge(r.Cosigner, "badge-m").AddTo(card);
                var col = Box("dk-rem-col").NoPick().AddTo(card);
                Bold(Dk.F("desk.remedial.waiting", Dk.Name(r.Cosigner)), "dk-rem-card-title").Plain().NoPick().AddTo(col);
                Text(Dk.F("desk.remedial.waiting_sub", Dk.TaskName(r.Task)), "dk-rem-card-sub").Plain().NoPick().AddTo(col);
                _timerOut = MemoBold("", "dk-rem-timer").NoPick().AddTo(card);
            }

            // start
            Dk.Heading(_body, Dk.T("desk.remedial.start"), Dk.T("desk.remedial.start_hint"));
            if (m.OutgoingRemedial != null) { Text(Dk.T("desk.remedial.pending_note"), "dk-empty").AddTo(_body); Dk.NoRich(_body); return; }
            if (m.RemedialsLeftThisBlock <= 0 || m.RemedialsLeftThisMatch <= 0) { Text(Dk.T("desk.remedial.none_left"), "dk-empty").AddTo(_body); return; }
            if (m.RemedialOptions.Count == 0) { Text(Dk.T("desk.remedial.no_options"), "dk-empty").AddTo(_body); return; }
            if (!m.RemedialOptions.Contains(_task)) _task = -1;
            var list = Box("dk-opts", "dk-opts-col").AddTo(_body);
            for (int i = 0; i < m.RemedialOptions.Count; i++)
            {
                int number = m.RemedialOptions[i];
                int output, max;
                string label = TaskLabel(v, number, out output, out max);
                var tile = Dk.Tile(() => { _task = number; _err = ""; _sig = int.MinValue; Refresh(Dk.View, true); }, "dk-opt").AddTo(list);
                tile.EnableInClassList("dk-opt-on", _task == number);
                Box("dk-opt-mark").NoPick().AddTo(tile);
                Bold(label, "dk-opt-text").NoPick().AddTo(tile);
                if (output >= 0) Tag(Dk.F("desk.remedial.output", output, max), "tag-grey").AddTo(tile);
            }
            Dk.Heading(_body, Dk.T("desk.remedial.who"), Dk.T("desk.remedial.who_hint"));
            var pick = Dk.Picker(_body, v, id => { _cosigner = id; _err = ""; _sig = int.MinValue; Refresh(Dk.View, true); }, false, "badge-m");
            pick(_cosigner);
            var foot = Box("dk-composer-foot").AddTo(_body);
            string why = _task < 0 ? Dk.T("desk.remedial.pick_task") : _cosigner < 0 ? Dk.T("desk.remedial.pick_who") : Dk.ComputerBlocked(v) ? Dk.T("desk.msg.blocked") : "";
            Text(_err.Length > 0 ? _err : why.Length > 0 ? why : Dk.T("desk.remedial.ready"), "dk-composer-status", _err.Length > 0 ? "dk-composer-status-bad" : "").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            var send = Btn(Dk.T("desk.remedial.send"), () => { _err = ""; Dk.Send(LineVerb.StartRemedial, _cosigner, _task); }, "btn-primary", "dk-composer-send").AddTo(foot);
            send.SetEnabled(why.Length == 0);
            Dk.NoRich(_body);
        }

        public override void Tick(LineView v)
        {
            var m = v.Mine;
            double exp = m.IncomingRemedial != null ? m.IncomingRemedial.ExpiresAt : m.OutgoingRemedial != null ? m.OutgoingRemedial.ExpiresAt : 0;
            if (exp <= 0) return;
            int s = Mathf.Max(0, Mathf.CeilToInt((float)(exp - Dk.Now)));
            if (s == _lastSec) return;
            _lastSec = s;
            _timerIn?.SetText(ClockInt(s));
            _timerOut?.SetText(ClockInt(s));
        }

        public override bool OnResult(LineActionResult r, LineAction req)
        {
            if (req.Verb != LineVerb.StartRemedial && req.Verb != LineVerb.RespondRemedial) return false;
            if (r.Accepted) { _task = -1; _cosigner = LineIds.None; _err = ""; _sig = int.MinValue; return false; }
            _err = Dk.T("desk.fail." + r.Failure);
            _sig = int.MinValue;
            var v = Dk.View;
            if (v != null) Refresh(v, true);
            return true;
        }
    }

    // =================================================================================================================
    // Dirty trick
    // =================================================================================================================

    public sealed class DeskTrickApp : DeskAppView
    {
        public DeskTrickApp(DeskUi ui) : base(ui, "dk-app-trick") { Build(); }
        public override DeskApp Id => DeskApp.Trick;
        public override string Icon => "trick_app";

        VisualElement _body;
        int _target = LineIds.None, _apparent = LineIds.None, _tpl = -1;
        bool _confirming;
        int _sig = int.MinValue;
        string _err = "";
        Dk.TextBox _box;
        TrickType _built;

        public override string Subtitle(LineView v) { return v.Mine.Trick == TrickType.None ? "" : Dk.TrickName(v.Mine.Trick); }

        void Build()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-trick-scroll").AddTo(Root);
            _body = scroll.contentContainer;
        }

        public override bool Back()
        {
            if (_confirming) { _confirming = false; ForceRebuild(); return true; }
            return false;
        }

        internal void MockPick(int target, int apparent, int tpl) { _target = target; _apparent = apparent; _tpl = tpl; ForceRebuild(); if (tpl >= 0 && _box != null) _box.SetValue(Dk.T("mail.template." + tpl)); }

        void ForceRebuild() { _sig = int.MinValue; var v = Dk.View; if (v != null) Refresh(v, true); }

        public override void OnShown() { ForceRebuild(); }

        static string Unavailable(LineView v)
        {
            var m = v.Mine;
            if (m.Trick == TrickType.None) return Dk.T("desk.trick.none");
            if (!v.Features.TricksUnlocked) return Dk.T("desk.trick.locked");
            if (m.TricksLeftThisWeek <= 0) return Dk.T("desk.trick.no_left");
            if (m.TrickUsedToday) return Dk.T("desk.trick.used_today");
            if (!LineClient.IsOfficePhase(v.Phase)) return Dk.T("desk.trick.not_now");
            if (Dk.ComputerBlocked(v)) return Dk.T("desk.msg.blocked");
            return "";
        }

        public override void Refresh(LineView v, bool force)
        {
            var m = v.Mine;
            int sig = (int)m.Trick * 31 + m.TricksLeftThisWeek * 7 + (m.TrickUsedToday ? 1 : 0) + (v.Features.TricksUnlocked ? 2 : 0) + _target * 13 + _apparent * 17 + _tpl * 19 + (_confirming ? 5 : 0) + _err.Length;
            sig = sig * 31 + (int)v.Phase + (Dk.ComputerBlocked(v) ? 1 : 0) + v.Players.Count;
            if (!force && sig == _sig) return;
            // the free-text box must survive refreshes while it is typed into: only rebuild when something visible changed
            _sig = sig;
            Build(v);
        }

        void Build(LineView v)
        {
            var m = v.Mine;
            var t = m.Trick;
            string text = _box != null ? _box.Value : "";
            _body.Clear();
            _box = null;
            if (t == TrickType.None) { Text(Dk.T("desk.trick.none"), "dk-empty").AddTo(_body); return; }
            if (t != _built) { _built = t; _target = _apparent = LineIds.None; _tpl = -1; _confirming = false; text = ""; }
            string unavailable = Unavailable(v);

            var top = Box("dk-trick-top").AddTo(_body);
            Dk.TrickCard(t, "dk-trick-card").AddTo(top);
            var col = Box("dk-trick-col").NoPick().AddTo(top);
            StampText(Loc.Upper(Dk.T("desk.trick.band")), "dk-section-band").NoPick().AddTo(col);
            Title(Dk.TrickName(t), "dk-trick-name").NoPick().AddTo(col);
            Text(Dk.TrickHow(t), "dk-trick-how").NoPick().AddTo(col);
            var chips = Box("dk-ev-info").NoPick().AddTo(col);
            Bold(Dk.F("desk.trick.left", m.TricksLeftThisWeek), "dk-ev-info-chip").NoPick().AddTo(chips);
            Bold(Dk.T(m.TrickUsedToday ? "desk.trick.used_chip" : "desk.trick.ready_chip"), "dk-ev-info-chip").NoPick().AddTo(chips);
            var warn = Box("dk-trick-warn").NoPick().AddTo(col);
            Dk.Icon("warning", "dk-trick-warn-icon").AddTo(warn);
            Text(Dk.F("desk.trick.warning", Dk.Config.CareerSabotagePenalty, Dk.Config.ExposedSabotageBlame), "dk-trick-warn-text").NoPick().AddTo(warn);

            if (unavailable.Length > 0) { Text(unavailable, "dk-composer-status", "dk-composer-status-bad").NoPick().AddTo(_body); Dk.NoRich(_body); return; }

            if (Dk.TrickNeedsTarget(t) && t != TrickType.SahteEposta)
            {
                Dk.Heading(_body, Dk.T("desk.trick.victim"));
                var pick = Dk.Picker(_body, v, id => { _target = id; if (_apparent == id) _apparent = LineIds.None; _err = ""; _confirming = false; ForceRebuild(); }, false, "badge-m");
                pick(_target);
            }
            if (t == TrickType.SahteEposta)
            {
                Dk.Heading(_body, Dk.T("desk.trick.apparent"), Dk.T("desk.trick.apparent_hint"));
                var ap = Dk.ChipPicker(_body, v, id => { _apparent = id; _err = ""; _confirming = false; ForceRebuild(); }, false, null);
                ap(_apparent);
                Dk.Heading(_body, Dk.T("desk.trick.body"), Dk.T("desk.trick.body_hint"));
                var tpls = Box("dk-chips").AddTo(_body);
                for (int i = 0; i < Dk.Config.MailTemplates; i++)
                {
                    int idx = i;
                    var chip = Dk.Tile(() => { _tpl = idx; if (_box != null) _box.SetValue(Dk.T("mail.template." + idx)); UpdateButtons(); }, "dk-chip", "dk-chip-tpl").AddTo(tpls);
                    chip.EnableInClassList("dk-chip-on", _tpl == i);
                    Bold(Dk.T("mail.template." + i + ".title"), "dk-chip-text").NoPick().AddTo(chip);
                }
                _box = Dk.TextInput(_body, Dk.Config.EmailMaxChars, true, Dk.T("desk.trick.placeholder"), "dk-composer-box", "dk-mail-box");
                _box.SetValue(text);
                _box.Changed = () => { if (_tpl >= 0 && _box.Value != Dk.T("mail.template." + _tpl)) { _tpl = -1; } _err = ""; UpdateButtons(); };
            }

            var foot = Box("dk-composer-foot").AddTo(_body);
            _status = Text("", "dk-composer-status").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _use = Btn(Dk.T("desk.trick.use"), OnUse, "btn-danger", "dk-composer-send").AddTo(foot);
            if (_confirming)
            {
                var cf = Box("dk-confirm").AddTo(_body);
                Title(Dk.T("desk.sab.title"), "dk-confirm-title").NoPick().AddTo(cf);
                Bold(Dk.TrickName(t) + (_target >= 0 ? " - " + Dk.Name(_target) : ""), "dk-confirm-opt").Plain().NoPick().AddTo(cf);
                Text(Dk.F("desk.trick.confirm", Dk.Config.CareerSabotagePenalty, Dk.Config.ExposedSabotageBlame), "dk-confirm-rule").NoPick().AddTo(cf);
                var row = Box("dk-confirm-row").AddTo(cf);
                Btn(Dk.T("desk.sab.back"), () => { _confirming = false; ForceRebuild(); }, "btn-secondary").AddTo(row);
                Btn(Dk.T("desk.trick.do"), DoUse, "btn-danger", "dk-confirm-do").AddTo(row);
                UiMotion.PopIn(cf, 0.2f, 0f, 0.92f);
            }
            UpdateButtons();
            Dk.NoRich(_body);
        }

        Label _status;
        Button _use;

        bool Ready(LineView v, out string why)
        {
            why = "";
            var t = v.Mine.Trick;
            // A fake email goes to everyone; its only target is the apparent sender (engine: UseTrick.Target).
            if (Dk.TrickNeedsTarget(t) && t != TrickType.SahteEposta && _target < 0) { why = Dk.T("desk.trick.pick_victim"); return false; }
            if (t == TrickType.SahteEposta)
            {
                if (_apparent < 0) { why = Dk.T("desk.trick.pick_apparent"); return false; }
                if (_box == null || _box.Value.Trim().Length == 0) { why = Dk.T("desk.trick.pick_body"); return false; }
            }
            return true;
        }

        void UpdateButtons()
        {
            var v = Dk.View;
            if (v == null || _use == null) return;
            string why;
            bool ok = Ready(v, out why);
            _use.SetEnabled(ok && !_confirming);
            _status.SetText(_err.Length > 0 ? _err : why.Length > 0 ? why : Dk.T("desk.trick.ready"));
            _status.EnableInClassList("dk-composer-status-bad", _err.Length > 0);
        }

        void OnUse()
        {
            var v = Dk.View;
            string why;
            if (v == null || !Ready(v, out why)) { Dk.Play("deny"); return; }
            _confirming = true;
            ForceRebuild();
        }

        void DoUse()
        {
            var v = Dk.View;
            string why;
            if (v == null || !Ready(v, out why)) { _confirming = false; ForceRebuild(); return; }
            var t = v.Mine.Trick;
            _err = "";
            if (t == TrickType.SahteEposta) Dk.Send(LineVerb.UseTrick, _apparent, _tpl, -1, -1, _box.Value.Trim());
            else Dk.Send(LineVerb.UseTrick, Dk.TrickNeedsTarget(t) ? _target : LineIds.None);
            _confirming = false;
            Dk.Play("stamp");
            ForceRebuild();
        }

        public override bool OnResult(LineActionResult r, LineAction req)
        {
            if (req.Verb != LineVerb.UseTrick) return false;
            if (r.Accepted) { _target = _apparent = LineIds.None; _tpl = -1; _confirming = false; _err = ""; ForceRebuild(); return false; }
            _confirming = false;
            _err = Dk.T("desk.fail." + r.Failure);
            ForceRebuild();
            return true;
        }
    }
}
