// Structural fixture for scripts/test-patch-locators.ps1.
// Mirrors the shapes Wand's minified bundles use, so every locator in
// EnhancerConfig must resolve against both the prettified and minified
// variants of this file.
const electron = require("electron");
const constants = require("shared/constants");
const remoteSources = require("shared/api/remote");

const handlers = {};

function expectUpdateFeedUrl(_, next) {
  return next();
}

function checkForUpdate(_) {
  return null;
}

function registerHandler(name, handler) {
  handlers[name] = handler;
}

function run() {
  return null;
}

electron.whenReady().then(run);

registerHandler("ACTION_CHECK_FOR_UPDATE", (e) => expectUpdateFeedUrl(e, (e) => checkForUpdate(e)));

class TrainerService {
  #e;
  #A;
  #D;
  #T;
  #F;
  #k;
  #M;
  #O;
  status = 0;

  requestRemoteAuthCode() {
    return this.#e.post("/v3/auth/remote_code")
  }

  setCurrentTrainer(e, t = null) {
    const s = e?.trainerId || null, i = (s ? e?.gameId : null) || null, a = (s ? e?.supportedVersions : null) || [];
    if (s === this.#T && t === this.#D) return;
    this.#T = s, this.#D = i;
  }

  #W() {
    this.#M && (this.#M.dispose(), this.#M = null), this.#F = Date.now().toString(), this.#T = null, this.#k = [], this.#D = null
  }

  #de(e, t) {
    t.push(e.onValueSet(e => { if (e.source === remoteSources.kL.Remote) return; this.#A?.send("client-value-changed", { instanceId: this.#F, name: e.name }) })), this.#z()
  }

  #z() {
    if (3 === this.status) {
      let e, t = !1;
      this.#A?.send("client-state", { instanceId: this.#F, trainerId: this.#T, trainerLoading: this.#D?.isLoading(), metadata: this.#D?.getMetadata(constants.vO), isTimeLimitExpired: "expired" === this.#O.timerState });
    }
  }
}

module.exports = { TrainerService };
