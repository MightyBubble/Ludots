(() => {
  const data = window.PLAYER_ACTION_UX_CATALOG;
  if (!data) throw new Error("PLAYER_ACTION_UX_CATALOG missing");

  const $ = (sel, root = document) => root.querySelector(sel);
  const navEl = $("#nav");
  const listEl = $("#list");
  const searchEl = $("#search");
  const statsEl = $("#stats");

  if (!navEl || !listEl || !searchEl || !statsEl) {
    throw new Error("catalog page shell missing required nodes (#nav/#list/#search/#stats)");
  }

  let activeCategory = "all";
  let query = "";
  let uid = 0;

  const teamColor = (team) => {
    if (team === "enemy") return "#ef6b6b";
    if (team === "neutral") return "#e0c35a";
    return "#5dce8f";
  };

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  /** Stage inset inside comic panel frame */
  const SX = 18;
  const SY = 36;
  const SW = 284;
  const SH = 150;

  function drawStage(view, gid) {
    if (view === "fps") {
      return `
        <rect x="${SX}" y="${SY}" width="${SW}" height="${SH}" fill="#141a22"/>
        <path d="M${SX} ${SY + 100} L${SX + 70} ${SY + 75} L${SX + 142} ${SY + 92} L${SX + 214} ${SY + 70} L${SX + SW} ${SY + 100} L${SX + SW} ${SY + SH} L${SX} ${SY + SH} Z" fill="#1c2633"/>
        <path d="M${SX} ${SY} L${SX + SW} ${SY} L${SX + SW} ${SY + 55} L${SX} ${SY + 72} Z" fill="#10151c" opacity="0.9"/>`;
    }
    if (view === "tps") {
      return `
        <defs>
          <linearGradient id="${gid}" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="#243041"/><stop offset="100%" stop-color="#151b24"/>
          </linearGradient>
        </defs>
        <rect x="${SX}" y="${SY}" width="${SW}" height="${SH}" fill="url(#${gid})"/>
        <ellipse cx="${SX + SW / 2}" cy="${SY + SH - 22}" rx="132" ry="24" fill="#0f141c" opacity="0.55"/>`;
    }
    return `
      <rect x="${SX}" y="${SY}" width="${SW}" height="${SH}" fill="#121820"/>
      <g opacity="0.22" stroke="#3a4658" stroke-width="1">
        ${Array.from({ length: 7 }, (_, i) => {
          const x = SX + ((i + 1) * SW) / 8;
          return `<line x1="${x}" y1="${SY}" x2="${x}" y2="${SY + SH}"/>`;
        }).join("")}
        ${Array.from({ length: 4 }, (_, i) => {
          const y = SY + ((i + 1) * SH) / 5;
          return `<line x1="${SX}" y1="${y}" x2="${SX + SW}" y2="${y}"/>`;
        }).join("")}
      </g>`;
  }

  function px(n) { return SX + (Number(n) / 100) * SW; }
  function py(n) { return SY + (Number(n) / 100) * SH; }

  function renderCast(cast = [], markerPrefix = "ah") {
    return cast.map((el) => {
      if (el.t === "unit") {
        const x = px(el.x), y = py(el.y);
        const s = 8 * (el.size || 1);
        const fill = teamColor(el.team);
        const sel = el.sel
          ? `<circle cx="${x}" cy="${y + s * 0.9}" r="${s + 4}" fill="none" stroke="#f0a35e" stroke-width="2" opacity="0.95"/>`
          : "";
        const face = ((el.face || 0) * Math.PI) / 180;
        const fx = x + Math.cos(face) * (s + 3);
        const fy = y + Math.sin(face) * (s + 3);
        return `${sel}
          <polygon points="${x},${y - s} ${x + s * 0.9},${y + s * 0.7} ${x - s * 0.9},${y + s * 0.7}" fill="${fill}" stroke="#0b0e13" stroke-width="1.2"/>
          <line x1="${x}" y1="${y}" x2="${fx}" y2="${fy}" stroke="#e8eef6" stroke-width="1.5" opacity="0.7"/>`;
      }
      if (el.t === "hero") {
        const x = px(el.x), y = py(el.y);
        const face = ((el.face || 0) * Math.PI) / 180;
        return `
          <ellipse cx="${x}" cy="${y + 10}" rx="10" ry="4" fill="#000" opacity="0.35"/>
          <circle cx="${x}" cy="${y}" r="9" fill="#6ec8ff" stroke="#0b0e13" stroke-width="1.4"/>
          <line x1="${x}" y1="${y}" x2="${x + Math.cos(face) * 16}" y2="${y + Math.sin(face) * 16}" stroke="#f0a35e" stroke-width="2.2"/>`;
      }
      if (el.t === "cursor") {
        const x = px(el.x), y = py(el.y);
        const down = el.mode === "down" || el.mode === "drag";
        return `
          <g transform="translate(${x},${y})">
            <path d="M0 0 L0 16 L4.5 13 L8 20 L11 18.5 L7.5 11.5 L13 11 Z" fill="${down ? "#f0a35e" : "#e8eef6"}" stroke="#0b0e13" stroke-width="1"/>
            ${el.mode === "drag" ? `<circle cx="2" cy="2" r="10" fill="none" stroke="#f0a35e" stroke-dasharray="2 2" opacity="0.7"/>` : ""}
          </g>`;
      }
      if (el.t === "box") {
        return `<rect x="${px(el.x)}" y="${py(el.y)}" width="${(Number(el.w) / 100) * SW}" height="${(Number(el.h) / 100) * SH}" fill="rgba(110,200,255,0.12)" stroke="#6ec8ff" stroke-width="1.6" stroke-dasharray="4 3"/>`;
      }
      if (el.t === "ring") {
        const color = el.kind === "lock" ? "#ef6b6b" : el.kind === "buff" ? "#6ec8ff" : "#f0a35e";
        return `<ellipse cx="${px(el.x)}" cy="${py(el.y) + 6}" rx="${el.r || 8}" ry="${(el.r || 8) * 0.45}" fill="none" stroke="${color}" stroke-width="2.2"/>`;
      }
      if (el.t === "crosshair") {
        const x = px(el.x), y = py(el.y);
        const c = el.locked ? "#ef6b6b" : "#e8eef6";
        return `
          <circle cx="${x}" cy="${y}" r="10" fill="none" stroke="${c}" stroke-width="1.6"/>
          <line x1="${x - 16}" y1="${y}" x2="${x - 6}" y2="${y}" stroke="${c}" stroke-width="1.6"/>
          <line x1="${x + 6}" y1="${y}" x2="${x + 16}" y2="${y}" stroke="${c}" stroke-width="1.6"/>
          <line x1="${x}" y1="${y - 16}" x2="${x}" y2="${y - 6}" stroke="${c}" stroke-width="1.6"/>
          <line x1="${x}" y1="${y + 6}" x2="${x}" y2="${y + 16}" stroke="${c}" stroke-width="1.6"/>`;
      }
      if (el.t === "cone") {
        const x = px(el.x), y = py(el.y);
        const ang = ((el.angle || 0) * Math.PI) / 180;
        const half = (((el.spread || 40) / 2) * Math.PI) / 180;
        const len = (el.length || 28) * 2.0;
        const x1 = x + Math.cos(ang - half) * len;
        const y1 = y + Math.sin(ang - half) * len;
        const x2 = x + Math.cos(ang + half) * len;
        const y2 = y + Math.sin(ang + half) * len;
        return `<path d="M${x} ${y} L${x1} ${y1} L${x2} ${y2} Z" fill="rgba(240,163,94,0.18)" stroke="#f0a35e" stroke-width="1.4"/>`;
      }
      if (el.t === "arrow") {
        const color = el.kind === "attack" ? "#ef6b6b" : "#6ec8ff";
        const mid = el.kind === "attack" ? "attack" : "move";
        return `<line x1="${px(el.x1)}" y1="${py(el.y1)}" x2="${px(el.x2)}" y2="${py(el.y2)}" stroke="${color}" stroke-width="2.4" marker-end="url(#${markerPrefix}-${mid})"/>`;
      }
      if (el.t === "path") {
        const pts = (el.points || []).map((p) => `${px(p[0])},${py(p[1])}`).join(" ");
        const color = el.kind === "lasso" ? "#6ec8ff" : el.kind === "arc" ? "#e0c35a" : "#6ec8ff";
        const dash = el.kind === "lasso" ? "4 3" : el.kind === "arc" ? "0" : "5 4";
        return `<polyline points="${pts}" fill="none" stroke="${color}" stroke-width="2" stroke-dasharray="${dash}"/>`;
      }
      if (el.t === "circle") {
        const color = el.ok ? "#5dce8f" : "#ef6b6b";
        return `<circle cx="${px(el.x)}" cy="${py(el.y)}" r="${el.r || 16}" fill="${color}" fill-opacity="0.14" stroke="${color}" stroke-width="1.8" stroke-dasharray="4 3"/>`;
      }
      if (el.t === "building") {
        const x = px(el.x), y = py(el.y);
        const fill = el.ghost ? "rgba(110,200,255,0.2)" : "#8aa0b8";
        const stroke = el.ghost ? "#6ec8ff" : "#0b0e13";
        return `<rect x="${x - 12}" y="${y - 12}" width="24" height="24" rx="3" fill="${fill}" stroke="${stroke}" stroke-width="1.5" stroke-dasharray="${el.ghost ? "3 2" : "0"}"/>`;
      }
      if (el.t === "stickL" || el.t === "stickR") {
        const left = el.t === "stickL";
        const cx = left ? SX + 34 : SX + SW - 34;
        const cy = SY + SH - 34;
        const nx = Math.max(-1, Math.min(1, el.nx || 0));
        const ny = Math.max(-1, Math.min(1, el.ny || 0));
        const kx = cx + nx * 14;
        const ky = cy + ny * 14;
        return `
          <circle cx="${cx}" cy="${cy}" r="20" fill="#0b0e13" stroke="#3a4658" stroke-width="2"/>
          <circle cx="${kx}" cy="${ky}" r="9" fill="${left ? "#6ec8ff" : "#f0a35e"}" stroke="#e8eef6" stroke-width="1"/>
          <text x="${cx}" y="${cy + 30}" text-anchor="middle" fill="#93a0b4" font-size="10" font-family="IBM Plex Mono, monospace">${left ? "L" : "R"}</text>`;
      }
      if (el.t === "badge") {
        const w = Math.min(150, 20 + String(el.text).length * 9);
        return `
          <rect x="${SX + 8}" y="${SY + 8}" width="${w}" height="22" rx="4" fill="#0b0e13" stroke="#f0a35e" stroke-width="1.3"/>
          <text x="${SX + 16}" y="${SY + 23}" fill="#f0a35e" font-size="11" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.text)}</text>`;
      }
      return "";
    }).join("\n");
  }

  function sprockets(side) {
    const x = side === "left" ? 5 : 311;
    return Array.from({ length: 8 }, (_, i) => {
      const y = 28 + i * 26;
      return `<rect x="${x}" y="${y}" width="6" height="10" rx="1.2" fill="#2a3340"/>`;
    }).join("");
  }

  function storyboardSvg(beat, index) {
    const id = `sb${++uid}`;
    const shot = String(index + 1).padStart(2, "0");
    const title = beat.title || `步骤 ${index + 1}`;
    const cap = `${beat.input} → ${beat.screen}`;
    const shortCap = cap.length > 42 ? `${cap.slice(0, 41)}…` : cap;
    return `
      <svg class="story-svg" viewBox="0 0 320 248" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="分镜 ${shot} ${esc(title)}">
        <defs>
          <marker id="${id}-move" markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto">
            <path d="M0,0 L6,3 L0,6 Z" fill="#6ec8ff"/>
          </marker>
          <marker id="${id}-attack" markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto">
            <path d="M0,0 L6,3 L0,6 Z" fill="#ef6b6b"/>
          </marker>
          <clipPath id="clip-${id}">
            <rect x="${SX}" y="${SY}" width="${SW}" height="${SH}"/>
          </clipPath>
        </defs>

        <!-- film plate -->
        <rect x="0" y="0" width="320" height="248" fill="#1a1f28"/>
        ${sprockets("left")}
        ${sprockets("right")}

        <!-- slate header -->
        <rect x="14" y="8" width="292" height="22" fill="#0d1117"/>
        <text x="22" y="23" fill="#f0a35e" font-size="12" font-family="IBM Plex Mono, monospace" font-weight="700">SHOT ${shot}</text>
        <text x="88" y="23" fill="#e8eef6" font-size="12" font-family="DM Sans, sans-serif" font-weight="600">${esc(title)}</text>
        <text x="298" y="23" text-anchor="end" fill="#66758a" font-size="10" font-family="IBM Plex Mono, monospace">${esc(beat.view || "topdown")}</text>

        <!-- comic outer / inner frame -->
        <rect x="14" y="32" width="292" height="158" fill="none" stroke="#0b0e13" stroke-width="4"/>
        <rect x="16" y="34" width="288" height="154" fill="none" stroke="#c9a27a" stroke-width="1.2" opacity="0.55"/>

        <g clip-path="url(#clip-${id})">
          ${drawStage(beat.view || "topdown", `gnd-${id}`)}
          ${renderCast(beat.cast || [], id)}
        </g>

        <!-- bottom dialogue / action strip -->
        <rect x="14" y="196" width="292" height="42" fill="#10151c" stroke="#2c3645" stroke-width="1"/>
        <text x="22" y="213" fill="#66758a" font-size="9" font-family="IBM Plex Mono, monospace" letter-spacing="1">ACTION</text>
        <text x="22" y="229" fill="#d7deea" font-size="11.5" font-family="DM Sans, sans-serif">${esc(shortCap)}</text>
      </svg>`;
  }

  /** Sanitize message/note text for Mermaid sequenceDiagram labels. */
  function mmdText(s) {
    return String(s ?? "")
      .replace(/[\r\n]+/g, " ")
      .replace(/[;#]/g, " ")
      .replace(/"/g, "'")
      .replace(/</g, "‹")
      .replace(/>/g, "›")
      .trim();
  }

  /**
   * Standard Mermaid sequenceDiagram:
   * 玩家 → 输入 → 画面 → 手感, one rect block per storyboard beat.
   */
  function sequenceMermaid(beats) {
    const lines = [
      "sequenceDiagram",
      "  actor P as 玩家",
      "  participant I as 输入",
      "  participant S as 画面",
      "  participant F as 手感",
    ];
    (beats || []).forEach((b, i) => {
      const title = mmdText(b.title || `步骤 ${i + 1}`);
      lines.push("  rect rgba(240, 163, 94, 0.08)");
      lines.push(`    Note over P,F: T${i + 1} ${title}`);
      lines.push(`    P->>I: ${mmdText(b.input)}`);
      lines.push(`    I->>S: ${mmdText(b.screen)}`);
      lines.push(`    S->>F: ${mmdText(b.feel)}`);
      lines.push("  end");
    });
    return lines.join("\n");
  }

  let mermaidReady = false;
  let mermaidSeq = 0;
  let mermaidIO = null;

  function ensureMermaid() {
    if (!window.mermaid) {
      throw new Error("mermaid 未加载：无法渲染标准时序图（检查 CDN / 网络）");
    }
    if (!mermaidReady) {
      window.mermaid.initialize({
        startOnLoad: false,
        securityLevel: "strict",
        theme: "dark",
        fontFamily: "DM Sans, PingFang SC, Noto Sans SC, sans-serif",
        sequence: {
          actorMargin: 48,
          messageMargin: 28,
          mirrorActors: false,
          bottomMarginAdj: 8,
          useMaxWidth: true,
        },
      });
      mermaidReady = true;
    }
  }

  async function renderOneMermaid(pre) {
    if (!pre || !pre.isConnected || !pre.hasAttribute("data-pending")) return;
    const src = pre.textContent;
    const id = `ux-seq-${++mermaidSeq}`;
    pre.removeAttribute("data-pending");
    try {
      const out = await window.mermaid.render(id, src);
      const wrap = document.createElement("div");
      wrap.className = "mermaid";
      wrap.setAttribute("role", "img");
      wrap.setAttribute("aria-label", "Mermaid 时序图");
      wrap.innerHTML = out.svg;
      pre.replaceWith(wrap);
    } catch (err) {
      const msg = err && err.message ? err.message : String(err);
      const fail = document.createElement("pre");
      fail.className = "mmd-error";
      fail.textContent = `Mermaid 时序图渲染失败 (${id}): ${msg}\n---\n${src}`;
      pre.replaceWith(fail);
      throw new Error(`Mermaid 时序图渲染失败 (${id}): ${msg}`);
    }
  }

  function paintMermaid() {
    ensureMermaid();
    if (mermaidIO) {
      mermaidIO.disconnect();
      mermaidIO = null;
    }
    const boxes = [...listEl.querySelectorAll(".mmd-box")];
    if (!boxes.length) return;

    mermaidIO = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        const pre = entry.target.querySelector("pre.mermaid[data-pending]");
        mermaidIO.unobserve(entry.target);
        if (!pre) continue;
        renderOneMermaid(pre).catch((err) => {
          console.error(err);
        });
      }
    }, { rootMargin: "240px 0px", threshold: 0.01 });

    boxes.forEach((box) => mermaidIO.observe(box));
  }

  function renderCase(c) {
    const tags = (c.genres || []).map((g) => `<span class="tag">${esc(g)}</span>`).join("");
    const panels = c.beats.map((b, i) => `
      <article class="panel">
        ${storyboardSvg(b, i)}
        <div class="panel-cap">
          <div class="cap-row"><span class="k">输入</span><span class="v">${esc(b.input)}</span></div>
          <div class="cap-row"><span class="k">画面</span><span class="v">${esc(b.screen)}</span></div>
          <div class="cap-row"><span class="k">手感</span><span class="v">${esc(b.feel)}</span></div>
        </div>
      </article>`).join("");
    const mmd = sequenceMermaid(c.beats);
    return `
      <article class="case-card" id="${esc(c.id)}" data-category="${esc(c.category)}">
        <div class="case-head">
          <h3>${esc(c.title)}</h3>
          <span class="case-id">${esc(c.id)}</span>
        </div>
        <p class="summary">${esc(c.summary)}</p>
        <div class="tags">${tags}</div>
        <p class="section-label">Mermaid 时序图 · 玩家 → 输入 → 画面 → 手感</p>
        <div class="mmd-box">
          <pre class="mermaid" data-pending="1">${esc(mmd)}</pre>
        </div>
        <p class="section-label">分镜胶片</p>
        <div class="storyboard" aria-label="分镜条">${panels}</div>
      </article>`;
  }

  function filtered() {
    const q = query.trim().toLowerCase();
    return data.cases.filter((c) => {
      if (activeCategory !== "all" && c.category !== activeCategory) return false;
      if (!q) return true;
      const blob = [c.title, c.summary, c.id, ...(c.genres || []), ...c.beats.flatMap((b) => [b.input, b.screen, b.feel])].join(" ").toLowerCase();
      return blob.includes(q);
    });
  }

  function renderNav() {
    const items = [{ id: "all", title: "全部" }, ...data.categories];
    navEl.innerHTML = `<h2>目录</h2>` + items.map((cat) => {
      const count = cat.id === "all" ? data.cases.length : data.cases.filter((c) => c.category === cat.id).length;
      return `<button type="button" data-cat="${esc(cat.id)}" class="${activeCategory === cat.id ? "active" : ""}">${esc(cat.title)} <span style="color:#66758a">(${count})</span></button>`;
    }).join("");
    navEl.querySelectorAll("button").forEach((btn) => {
      btn.addEventListener("click", () => {
        activeCategory = btn.dataset.cat;
        render();
      });
    });
  }

  async function render() {
    renderNav();
    const cases = filtered();
    statsEl.innerHTML = `
      <span class="chip">动作 <strong>${data.cases.length}</strong></span>
      <span class="chip">分镜格 <strong>${data.cases.reduce((n, c) => n + c.beats.length, 0)}</strong></span>
      <span class="chip">当前显示 <strong>${cases.length}</strong></span>`;
    listEl.innerHTML = cases.length
      ? cases.map(renderCase).join("")
      : `<div class="empty">没有匹配的动作。试试别的关键词。</div>`;
    paintMermaid();
  }

  let searchTimer = 0;
  searchEl.addEventListener("input", (e) => {
    query = e.target.value;
    window.clearTimeout(searchTimer);
    searchTimer = window.setTimeout(() => { render(); }, 120);
  });

  render();
})();

