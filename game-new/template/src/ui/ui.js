// DOM UI. Draws what the logic says (state.ui decides which popup is open) and sends clicks as input events.
// Layout rules (so Unity can rebuild it automatically from `npm run t2u:export`):
//  - every element Unity needs has data-ui="<name>"; screens/popups have data-ui-screen="<name>";
//  - buttons have data-ui-button="<id>" and send {t:'click', id};
//  - sizes in calc(N * var(--u)) where --u = frame width / 1080 (design at 1080 px wide).
export function createUi(root, send) {
  const $ = (sel) => root.querySelector(sel);
  root.querySelectorAll('[data-ui-button]').forEach((el) =>
    el.addEventListener('click', (e) => {
      e.stopPropagation();
      send({ t: 'click', id: el.dataset.uiButton });
    })
  );
  const texts = {};
  const setText = (name, v) => {
    if (texts[name] === v) return;
    texts[name] = v;
    $(`[data-ui="${name}"]`).textContent = v;
  };
  return {
    update(g) {
      setText('hud.level', `Level ${g.levelNo}`);
      setText('hud.score', `${g.score}/${g.goal}`);
      setText('hud.time', `${Math.ceil(g.time)}`);
      for (let i = 1; i <= 3; i++) $(`[data-ui="win.star${i}"]`).classList.toggle('on', g.stars >= i);
      for (const s of ['win', 'lose', 'ad']) $(`[data-ui-screen="${s}"]`).hidden = g.ui !== s;
      $('[data-ui-button="moreTime"]').hidden = g.revived;
    },
  };
}
