(() => {
  const data = window.PLAYER_ACTION_UX_CATALOG;
  if (!data) throw new Error("PLAYER_ACTION_UX_CATALOG missing");

  const $ = (sel, root = document) => root.querySelector(sel);
  const navEl = $("#nav");
  const listEl = $("#list");
  const detailEl = $("#detail");
  const searchEl = $("#search");
  const statsEl = $("#stats");

  if (!navEl || !listEl || !detailEl || !searchEl || !statsEl) {
    throw new Error("catalog page shell missing required nodes (#nav/#list/#detail/#search/#stats)");
  }

  if (!Array.isArray(data.actions) || !data.actions.length) {
    throw new Error("PLAYER_ACTION_UX_CATALOG.actions missing — regenerate catalog-data.js");
  }

  const casesById = Object.fromEntries(data.cases.map((c) => [c.id, c]));
  const actionsByKey = Object.fromEntries(data.actions.map((a) => [a.key, a]));

  if (!Array.isArray(data.views) || !data.views.length) {
    throw new Error("PLAYER_ACTION_UX_CATALOG.views missing — regenerate catalog-data.js");
  }
  if (!Array.isArray(data.cursorModes) || !data.cursorModes.length) {
    throw new Error("PLAYER_ACTION_UX_CATALOG.cursorModes missing — regenerate catalog-data.js");
  }
  const VIEW_LABEL = Object.fromEntries(data.views.map((v) => [v.id, v.label]));
  const CURSOR_MODES = data.cursorModes.slice();

  function viewLabel(view) {
    const label = VIEW_LABEL[view];
    if (!label) throw new Error(`分镜视角未知：${view} — 修 catalog 生成脚本`);
    return label;
  }

  /** Default to first real category so the page is not a 150-card dump. */
  let activeCategory = (data.categories[0] && data.categories[0].id) || "all";
  let selectedActionKey = null;
  let selectedPlatform = null;
  let activeBeat = 0;
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
        // 倒地：躺平的椭圆，一眼和站着的三角分开
        if (el.state === "downed") {
          return `${sel}
            <ellipse cx="${x}" cy="${y + s * 0.5}" rx="${s * 1.15}" ry="${s * 0.5}" fill="${fill}" stroke="#0b0e13" stroke-width="1.2" opacity="0.75"/>
            <text x="${x}" y="${y - s * 0.4}" text-anchor="middle" fill="#ef6b6b" font-size="9" font-family="DM Sans, sans-serif" font-weight="700">倒地</text>`;
        }
        // 空中：抬高本体，地面留投影，看得出「他在天上」
        const air = el.layer === "air";
        const cy = air ? y - 12 : y;
        const shadow = air
          ? `<ellipse cx="${x}" cy="${y + 8}" rx="${s * 0.8}" ry="${s * 0.32}" fill="#000" opacity="0.4"/>
             <line x1="${x}" y1="${cy + s * 0.8}" x2="${x}" y2="${y + 6}" stroke="#66758a" stroke-width="1" stroke-dasharray="2 2"/>`
          : "";
        const badgeAir = air
          ? `<text x="${x}" y="${cy - s - 4}" text-anchor="middle" fill="#6ec8ff" font-size="9" font-family="DM Sans, sans-serif" font-weight="700">空中</text>`
          : "";
        const outline = el.highlight
          ? `<polygon points="${x},${cy - s - 3} ${x + s * 1.2},${cy + s * 0.95} ${x - s * 1.2},${cy + s * 0.95}" fill="none" stroke="#e8eef6" stroke-width="1.6" stroke-dasharray="3 2"/>`
          : "";
        const role = el.role
          ? `<text x="${x}" y="${cy + s * 0.55}" text-anchor="middle" fill="#0b0e13" font-size="8.5" font-family="IBM Plex Mono, monospace" font-weight="700">${esc(el.role)}</text>`
          : "";
        return `${sel}${shadow}
          <polygon points="${x},${cy - s} ${x + s * 0.9},${cy + s * 0.7} ${x - s * 0.9},${cy + s * 0.7}" fill="${fill}" stroke="#0b0e13" stroke-width="1.2"/>
          ${role}${outline}
          <line x1="${x}" y1="${cy}" x2="${fx}" y2="${air ? fy - 12 : fy}" stroke="#e8eef6" stroke-width="1.5" opacity="0.7"/>
          ${badgeAir}`;
      }
      if (el.t === "hero") {
        const x = px(el.x), y = py(el.y);
        const face = ((el.face || 0) * Math.PI) / 180;
        const ghost = el.state === "ghost";
        // form=alt 换了形态：轮廓从圆变成带尖角，光看剪影就知道换了个身子
        const body = el.form === "alt"
          ? `<polygon points="${Array.from({ length: 8 }, (_, i) => {
              const a2 = (i / 8) * Math.PI * 2 - Math.PI / 2;
              const rr = i % 2 ? 6 : 12;
              return `${x + Math.cos(a2) * rr},${y + Math.sin(a2) * rr}`;
            }).join(" ")}" fill="#c78bff" stroke="#0b0e13" stroke-width="1.4"/>`
          : `<circle cx="${x}" cy="${y}" r="9" fill="${ghost ? "rgba(110,200,255,0.28)" : "#6ec8ff"}"
              stroke="${ghost ? "#6ec8ff" : "#0b0e13"}" stroke-width="1.4" stroke-dasharray="${ghost ? "3 2" : "0"}"/>`;
        return `
          <ellipse cx="${x}" cy="${y + 10}" rx="10" ry="4" fill="#000" opacity="${ghost ? 0.12 : 0.35}"/>
          ${body}
          <line x1="${x}" y1="${y}" x2="${x + Math.cos(face) * 16}" y2="${y + Math.sin(face) * 16}" stroke="#f0a35e" stroke-width="2.2" opacity="${ghost ? 0.5 : 1}"/>`;
      }
      if (el.t === "vehicle") {
        const x = px(el.x), y = py(el.y);
        const kind = el.kind || "car";
        let body;
        if (kind === "drone") {
          // 四轴无人机：接管轰炸时用，别画成地上的车
          body = `
            <line x1="${x - 16}" y1="${y - 10}" x2="${x + 16}" y2="${y + 10}" stroke="#9fb0c6" stroke-width="2"/>
            <line x1="${x + 16}" y1="${y - 10}" x2="${x - 16}" y2="${y + 10}" stroke="#9fb0c6" stroke-width="2"/>
            <rect x="${x - 8}" y="${y - 5}" width="16" height="10" rx="3" fill="#7e8fa6" stroke="#0b0e13" stroke-width="1.3"/>
            <circle cx="${x - 14}" cy="${y - 9}" r="4" fill="none" stroke="#6ec8ff" stroke-width="1.4"/>
            <circle cx="${x + 14}" cy="${y - 9}" r="4" fill="none" stroke="#6ec8ff" stroke-width="1.4"/>
            <circle cx="${x - 14}" cy="${y + 9}" r="4" fill="none" stroke="#6ec8ff" stroke-width="1.4"/>
            <circle cx="${x + 14}" cy="${y + 9}" r="4" fill="none" stroke="#6ec8ff" stroke-width="1.4"/>`;
        } else if (kind === "turret") {
          body = `<rect x="${x - 13}" y="${y - 9}" width="26" height="18" rx="4" fill="#7e8fa6" stroke="#0b0e13" stroke-width="1.4"/>
             <circle cx="${x}" cy="${y}" r="6.5" fill="#5a6b80" stroke="#0b0e13" stroke-width="1.2"/>
             <line x1="${x}" y1="${y}" x2="${x + 20}" y2="${y}" stroke="#5a6b80" stroke-width="4.5"/>`;
        } else {
          body = `<rect x="${x - 17}" y="${y - 8}" width="34" height="16" rx="4" fill="#7e8fa6" stroke="#0b0e13" stroke-width="1.4"/>
             <rect x="${x - 7}" y="${y - 13}" width="15" height="8" rx="2" fill="#9fb0c6" stroke="#0b0e13" stroke-width="1.1"/>
             <circle cx="${x - 10}" cy="${y + 9}" r="4" fill="#222b36" stroke="#0b0e13" stroke-width="1"/>
             <circle cx="${x + 10}" cy="${y + 9}" r="4" fill="#222b36" stroke="#0b0e13" stroke-width="1"/>
             ${kind === "tank" ? `<line x1="${x + 4}" y1="${y - 9}" x2="${x + 26}" y2="${y - 9}" stroke="#5a6b80" stroke-width="4"/>` : ""}`;
        }
        const seat = el.occupied
          ? `<circle cx="${x}" cy="${y - 16}" r="5" fill="#6ec8ff" stroke="#0b0e13" stroke-width="1.2"/>`
          : "";
        return `<ellipse cx="${x}" cy="${y + 12}" rx="19" ry="5" fill="#000" opacity="0.35"/>${body}${seat}`;
      }
      if (el.t === "fog") {
        const w = ((el.w == null ? 40 : el.w) / 100) * SW;
        const h = ((el.h == null ? 100 : el.h) / 100) * SH;
        const x = px(el.x == null ? 60 : el.x);
        const y = py(el.y == null ? 0 : el.y);
        return `
          <rect x="${x}" y="${y}" width="${w}" height="${h}" fill="rgba(8,10,14,0.82)"/>
          <text x="${x + w / 2}" y="${y + h / 2}" text-anchor="middle" fill="#66758a" font-size="11" font-family="DM Sans, sans-serif" font-weight="600">战争迷雾</text>`;
      }
      if (el.t === "marker") {
        const x = px(el.x), y = py(el.y);
        const glyph = { skull: "☠", moon: "☾", star: "★", cross: "✚", square: "◆", eye: "◉" }[el.icon] || "★";
        return `
          <circle cx="${x}" cy="${y}" r="9" fill="#0b0e13" stroke="#e0c35a" stroke-width="1.6"/>
          <text x="${x}" y="${y + 4}" text-anchor="middle" fill="#e0c35a" font-size="11" font-family="DM Sans, sans-serif" font-weight="700">${glyph}</text>
          <text x="${x}" y="${y - 13}" text-anchor="middle" fill="#e0c35a" font-size="8.5" font-family="DM Sans, sans-serif">${esc(el.label || "团队标记")}</text>`;
      }
      if (el.t === "playertag") {
        const x = px(el.x), y = py(el.y);
        const palette = { p1: "#6ec8ff", p2: "#f0a35e", p3: "#5dce8f", p4: "#c78bff" };
        const c = palette[el.color] || palette.p1;
        const w = 8 + String(el.label || "P1").length * 7;
        return `
          <rect x="${x - w / 2}" y="${y - 9}" width="${w}" height="14" rx="3" fill="#0b0e13" stroke="${c}" stroke-width="1.6"/>
          <text x="${x}" y="${y + 1.5}" text-anchor="middle" fill="${c}" font-size="9.5" font-family="IBM Plex Mono, monospace" font-weight="700">${esc(el.label || "P1")}</text>`;
      }
      if (el.t === "splitscreen") {
        const mode = el.mode || "v";
        const label = (t, x, y) => `<text x="${x}" y="${y}" fill="#93a0b4" font-size="10" font-family="IBM Plex Mono, monospace" font-weight="700">${t}</text>`;
        if (mode === "shared") {
          return `
            <rect x="${SX + 4}" y="${SY + 4}" width="${SW - 8}" height="${SH - 8}" fill="none" stroke="#93a0b4" stroke-width="1.4" stroke-dasharray="6 4"/>
            ${label("共享一块屏", SX + 10, SY + 18)}`;
        }
        if (mode === "h") {
          const my = SY + SH / 2;
          return `
            <line x1="${SX}" y1="${my}" x2="${SX + SW}" y2="${my}" stroke="#0b0e13" stroke-width="4"/>
            ${label("P1", SX + 8, SY + 16)}${label("P2", SX + 8, my + 16)}`;
        }
        const mx = SX + SW / 2;
        return `
          <line x1="${mx}" y1="${SY}" x2="${mx}" y2="${SY + SH}" stroke="#0b0e13" stroke-width="4"/>
          ${label("P1", SX + 8, SY + 16)}${label("P2", mx + 8, SY + 16)}`;
      }
      if (el.t === "padslot") {
        const states = Array.isArray(el.states) ? el.states : [];
        const w = 40, h = 24, gap = 6;
        const total = states.length * w + (states.length - 1) * gap;
        const x0 = SX + SW / 2 - total / 2;
        const y0 = SY + SH - h - 6;
        return states.map((st, i) => {
          const x = x0 + i * (w + gap);
          const c = st === "joined" ? "#5dce8f" : st === "lost" ? "#ef6b6b" : "#66758a";
          const text = st === "joined" ? `P${i + 1}` : st === "lost" ? "断开" : "按键加入";
          return `
            <rect x="${x}" y="${y0}" width="${w}" height="${h}" rx="4" fill="#0b0e13" stroke="${c}" stroke-width="${st === "waiting" ? 1.2 : 1.8}" stroke-dasharray="${st === "waiting" ? "3 2" : "0"}"/>
            <text x="${x + w / 2}" y="${y0 + h / 2 + 3.5}" text-anchor="middle" fill="${c}" font-size="${text.length > 2 ? 8 : 10}" font-family="DM Sans, sans-serif" font-weight="700">${text}</text>`;
        }).join("");
      }
      if (el.t === "extractzone") {
        // 撤离点：位置 + 开没开 + 还剩多久
        const x = px(el.x), y = py(el.y);
        const st = { open: ["#5dce8f", "可撤离"], closing: ["#e0c35a", "即将关闭"], closed: ["#ef6b6b", "已关闭"] };
        const [col, word] = st[el.state] || st.open;
        return `
          <path d="M${x - 20} ${y + 12} L${x - 20} ${y - 10} L${x + 20} ${y - 10} L${x + 20} ${y + 12} Z"
            fill="rgba(11,14,19,0.5)" stroke="${col}" stroke-width="2" stroke-dasharray="6 4"/>
          <path d="M${x - 8} ${y + 4} L${x} ${y - 5} L${x + 8} ${y + 4}" fill="none" stroke="${col}" stroke-width="2.2"/>
          <text x="${x}" y="${y - 15}" text-anchor="middle" fill="${col}" font-size="9" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.label || "撤离点")}</text>
          <text x="${x}" y="${y + 24}" text-anchor="middle" fill="${col}" font-size="8.5" font-family="DM Sans, sans-serif">${esc(el.note || word)}</text>`;
      }
      if (el.t === "cmdinput") {
        // 格斗指令输入：方向序列 + 按键，以及这次搓成没搓成
        const x = px(el.x), y = py(el.y);
        const seq = el.seq || [];
        const okCol = el.ok === false ? "#ef6b6b" : "#5dce8f";
        let out = "", cx = x;
        seq.forEach((s, i) => {
          const w = 20;
          out += `
            <rect x="${cx}" y="${y - 9}" width="${w}" height="18" rx="3" fill="#0b0e13"
              stroke="${el.failAt === i ? "#ef6b6b" : "#6ec8ff"}" stroke-width="${el.failAt === i ? 2 : 1.2}"/>
            <text x="${cx + w / 2}" y="${y + 4}" text-anchor="middle"
              fill="${el.failAt === i ? "#ef6b6b" : "#6ec8ff"}" font-size="10" font-family="DM Sans, sans-serif" font-weight="700">${esc(s)}</text>`;
          if (i < seq.length - 1) {
            out += `<text x="${cx + w + 3}" y="${y + 4}" fill="#3a4658" font-size="9" font-family="DM Sans, sans-serif">›</text>`;
          }
          cx += w + 10;
        });
        out += `<text x="${x}" y="${y - 14}" fill="${okCol}" font-size="8.5" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.label || (el.ok === false ? "没搓出来" : "指令成立"))}</text>`;
        return out;
      }
      if (el.t === "framebar") {
        // 帧优势：这一下打完，谁先能动
        const x = px(el.x), y = py(el.y);
        const W = 130, H = 10;
        const mine = Math.max(0, Math.min(1, el.mine == null ? 0.4 : el.mine));
        const theirs = Math.max(0, Math.min(1, el.theirs == null ? 0.7 : el.theirs));
        const better = mine < theirs;
        return `
          <text x="${x}" y="${y - 16}" fill="#93a0b4" font-size="8.5" font-family="IBM Plex Mono, monospace">硬直长短</text>
          <text x="${x - 4}" y="${y + 8}" text-anchor="end" fill="#6ec8ff" font-size="8.5" font-family="DM Sans, sans-serif">我</text>
          <rect x="${x}" y="${y}" width="${W}" height="${H}" rx="3" fill="#0b0e13" stroke="#2c3645" stroke-width="1"/>
          <rect x="${x + 1}" y="${y + 1}" width="${(W - 2) * mine}" height="${H - 2}" rx="2" fill="#6ec8ff"/>
          <text x="${x - 4}" y="${y + H + 12}" text-anchor="end" fill="#ef6b6b" font-size="8.5" font-family="DM Sans, sans-serif">他</text>
          <rect x="${x}" y="${y + H + 4}" width="${W}" height="${H}" rx="3" fill="#0b0e13" stroke="#2c3645" stroke-width="1"/>
          <rect x="${x + 1}" y="${y + H + 5}" width="${(W - 2) * theirs}" height="${H - 2}" rx="2" fill="#ef6b6b"/>
          <text x="${x + W + 6}" y="${y + H + 2}" fill="${better ? "#5dce8f" : "#e0c35a"}" font-size="9" font-family="DM Sans, sans-serif" font-weight="700">${better ? "我先能动" : "他先能动"}</text>`;
      }
      if (el.t === "takeover") {
        // 我现在操控的不是自己：整屏加一圈边框点明
        return `
          <rect x="${SX + 2}" y="${SY + 2}" width="${SW - 4}" height="${SH - 4}" fill="none"
            stroke="#e0c35a" stroke-width="2.4" stroke-dasharray="10 5"/>
          <rect x="${SX + SW / 2 - 52}" y="${SY + 3}" width="104" height="17" rx="3" fill="#0b0e13" stroke="#e0c35a" stroke-width="1.3"/>
          <text x="${SX + SW / 2}" y="${SY + 15}" text-anchor="middle" fill="#e0c35a" font-size="9.5" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.label || "你在操控别的东西")}</text>`;
      }
      if (el.t === "buffchip") {
        // 状态图标 + 层数：叠层、引爆、驱散的目标就是它
        const x = px(el.x), y = py(el.y);
        const dot = el.kind === "buff" ? "#5dce8f" : el.kind === "shield" ? "#6ec8ff" : "#c78bff";
        const w = 14 + String(el.label || "").length * 8.5;
        return `
          <rect x="${x - w / 2}" y="${y - 9}" width="${w}" height="18" rx="4"
            fill="rgba(11,14,19,0.92)" stroke="${dot}" stroke-width="1.5"/>
          <circle cx="${x - w / 2 + 8}" cy="${y}" r="3.4" fill="${dot}"/>
          <text x="${x - w / 2 + 15}" y="${y + 3.5}" fill="${dot}" font-size="9.5" font-family="DM Sans, sans-serif">${esc(el.label || "")}</text>
          ${el.stacks != null ? `<circle cx="${x + w / 2 - 1}" cy="${y - 8}" r="7.5" fill="#0b0e13" stroke="${dot}" stroke-width="1.4"/>
            <text x="${x + w / 2 - 1}" y="${y - 4.5}" text-anchor="middle" fill="${dot}" font-size="9" font-family="IBM Plex Mono, monospace" font-weight="700">${esc(el.stacks)}</text>` : ""}`;
      }
      if (el.t === "projectile") {
        // 飞在空中的投射物：它本身可以成为目标（打掉 / 反弹）
        const x = px(el.x), y = py(el.y);
        const a2 = ((el.angle || 0) * Math.PI) / 180;
        const tx = x + Math.cos(a2) * 13, ty = y + Math.sin(a2) * 13;
        const bx = x - Math.cos(a2) * 9, by = y - Math.sin(a2) * 9;
        return `
          <line x1="${bx - Math.cos(a2) * 9}" y1="${by - Math.sin(a2) * 9}" x2="${bx}" y2="${by}"
            stroke="#f0a35e" stroke-width="1.4" stroke-dasharray="3 3" opacity="0.7"/>
          <polygon points="${tx},${ty} ${bx + Math.cos(a2 + 2.4) * 7},${by + Math.sin(a2 + 2.4) * 7} ${bx + Math.cos(a2 - 2.4) * 7},${by + Math.sin(a2 - 2.4) * 7}"
            fill="#f0a35e" stroke="#0b0e13" stroke-width="1"/>
          ${el.label ? `<text x="${x}" y="${y - 13}" text-anchor="middle" fill="#f0a35e" font-size="8.5" font-family="DM Sans, sans-serif">${esc(el.label)}</text>` : ""}`;
      }
      if (el.t === "elementmark") {
        // 元素附着：两种附着凑在一起才会起反应
        const x = px(el.x), y = py(el.y);
        const map = { fire: ["#ef6b6b", "火"], water: ["#6ec8ff", "水"], ice: ["#a9dcff", "冰"], lightning: ["#e0c35a", "雷"] };
        const [col, ch] = map[el.kind] || map.fire;
        return `
          <circle cx="${x}" cy="${y}" r="8" fill="rgba(11,14,19,0.9)" stroke="${col}" stroke-width="1.8"/>
          <text x="${x}" y="${y + 3.5}" text-anchor="middle" fill="${col}" font-size="9.5" font-family="DM Sans, sans-serif" font-weight="700">${ch}</text>`;
      }
      if (el.t === "terrain") {
        // 技能造出来的地形：目标是环境本身
        const x = px(el.x), y = py(el.y);
        if (el.kind === "wall") {
          return `
            <rect x="${x - 4}" y="${y - 20}" width="8" height="40" rx="2" fill="#7e8fa6" stroke="#0b0e13" stroke-width="1.4"/>
            <text x="${x}" y="${y - 25}" text-anchor="middle" fill="#e0c35a" font-size="9" font-family="DM Sans, sans-serif">${esc(el.label || "墙")}</text>`;
        }
        if (el.kind === "pit") {
          return `
            <ellipse cx="${x}" cy="${y}" rx="20" ry="9" fill="#080a0e" stroke="#3a4658" stroke-width="1.6"/>
            <text x="${x}" y="${y + 20}" text-anchor="middle" fill="#93a0b4" font-size="9" font-family="DM Sans, sans-serif">${esc(el.label || "坑")}</text>`;
        }
        return `
          <ellipse cx="${x}" cy="${y}" rx="22" ry="10" fill="rgba(169,220,255,0.28)" stroke="#a9dcff" stroke-width="1.6" stroke-dasharray="4 3"/>
          <text x="${x}" y="${y + 21}" text-anchor="middle" fill="#a9dcff" font-size="9" font-family="DM Sans, sans-serif">${esc(el.label || "冰面")}</text>`;
      }
      if (el.t === "summon") {
        // 自己造出来的东西：图腾 / 守卫，可以成为自己技能的目标
        const x = px(el.x), y = py(el.y);
        return `
          <ellipse cx="${x}" cy="${y + 10}" rx="9" ry="3.5" fill="#000" opacity="0.35"/>
          <polygon points="${x},${y - 11} ${x + 8},${y + 6} ${x - 8},${y + 6}" fill="none" stroke="#5dce8f" stroke-width="2" stroke-dasharray="4 2"/>
          <circle cx="${x}" cy="${y - 1}" r="3" fill="#5dce8f"/>
          ${el.label ? `<text x="${x}" y="${y - 16}" text-anchor="middle" fill="#5dce8f" font-size="9" font-family="DM Sans, sans-serif" font-weight="600">${esc(el.label)}</text>` : ""}`;
      }
      if (el.t === "inputwindow") {
        // 动作时间轴：前摇 / 判定 / 后摆，以及能提前按的那一段缓存窗
        const x = px(el.x), y = py(el.y);
        const phases = el.phases || [];
        const total = phases.reduce((s, p) => s + (p.w || 1), 0);
        const W = 150, H = 14;
        let cx = x, out = "";
        const col = { startup: "#66758a", active: "#f0a35e", recovery: "#3a4658", buffer: "#5dce8f" };
        phases.forEach((p) => {
          const pw = (W * (p.w || 1)) / total;
          out += `
            <rect x="${cx}" y="${y}" width="${pw}" height="${H}" fill="${p.kind === "buffer" ? "rgba(93,206,143,0.22)" : "rgba(18,24,32,0.8)"}"
              stroke="${col[p.kind] || "#3a4658"}" stroke-width="1.3" stroke-dasharray="${p.kind === "buffer" ? "3 2" : "0"}"/>
            <text x="${cx + pw / 2}" y="${y + 10}" text-anchor="middle" fill="${col[p.kind] || "#93a0b4"}" font-size="8" font-family="DM Sans, sans-serif">${esc(p.name)}</text>`;
          cx += pw;
        });
        if (el.pressAt != null) {
          const mx = x + W * el.pressAt;
          out += `
            <line x1="${mx}" y1="${y - 7}" x2="${mx}" y2="${y + H + 5}" stroke="#6ec8ff" stroke-width="2"/>
            <text x="${mx}" y="${y - 10}" text-anchor="middle" fill="#6ec8ff" font-size="8.5" font-family="DM Sans, sans-serif" font-weight="700">我按了</text>`;
        }
        if (el.fireAt != null) {
          const fx = x + W * el.fireAt;
          out += `
            <line x1="${fx}" y1="${y - 7}" x2="${fx}" y2="${y + H + 5}" stroke="#f0a35e" stroke-width="2" stroke-dasharray="2 2"/>
            <text x="${fx}" y="${y + H + 15}" text-anchor="middle" fill="#f0a35e" font-size="8.5" font-family="DM Sans, sans-serif" font-weight="700">这里才放出</text>`;
        }
        return out;
      }
      if (el.t === "gridmap") {
        // 战棋格盘：目标是「哪几格」，不是连续坐标
        const x0 = px(el.x), y0 = py(el.y);
        const cw = el.cw || 22, ch = el.ch || 14;
        const kinds = {
          move: ["rgba(110,200,255,0.20)", "#6ec8ff"],
          attack: ["rgba(239,107,107,0.20)", "#ef6b6b"],
          blocked: ["rgba(60,70,84,0.55)", "#3a4658"],
          occupied: ["rgba(224,195,90,0.18)", "#e0c35a"],
          insured: ["rgba(93,206,143,0.22)", "#5dce8f"],
          self: ["rgba(240,163,94,0.28)", "#f0a35e"],
        };
        let out = "";
        for (let r = 0; r < el.rows; r++) {
          for (let q = 0; q < el.cols; q++) {
            const k = (el.cells || {})[`${q},${r}`];
            const [fill, stroke] = kinds[k] || ["rgba(18,24,32,0.5)", "#2c3645"];
            out += `<rect x="${x0 + q * cw}" y="${y0 + r * ch}" width="${cw - 2}" height="${ch - 2}"
              fill="${fill}" stroke="${stroke}" stroke-width="${k ? 1.4 : 0.9}"/>`;
          }
        }
        return out;
      }
      if (el.t === "timeline") {
        // 行动条：谁下一个出手。目标可以是「顺序里的位置」
        const entries = el.entries || [];
        const bw = 34, gap = 4;
        const total = entries.length * bw + (entries.length - 1) * gap;
        const sx = SX + SW / 2 - total / 2;
        const sy = SY + 8;
        let out = `<text x="${SX + 8}" y="${sy + 12}" fill="#66758a" font-size="8.5" font-family="IBM Plex Mono, monospace">出手顺序</text>`;
        entries.forEach((e, i) => {
          const bx = sx + i * (bw + gap);
          const now = el.current === i;
          const col = e.me ? "#6ec8ff" : "#ef6b6b";
          out += `
            <rect x="${bx}" y="${sy}" width="${bw}" height="18" rx="3"
              fill="${now ? "rgba(240,163,94,0.22)" : "#0b0e13"}"
              stroke="${now ? "#f0a35e" : col}" stroke-width="${now ? 2 : 1.2}"/>
            <text x="${bx + bw / 2}" y="${sy + 13}" text-anchor="middle" fill="${now ? "#f0a35e" : col}"
              font-size="9" font-family="DM Sans, sans-serif" font-weight="${now ? 700 : 400}">${esc(e.name)}</text>`;
        });
        return out;
      }
      if (el.t === "region") {
        // 区域 / 省份：目标是一个行政单元，有归属
        const x = px(el.x), y = py(el.y);
        const w = el.w || 46, h = el.h || 30;
        const owner = { mine: "#6ec8ff", rival: "#ef6b6b", neutral: "#93a0b4" }[el.owner] || "#93a0b4";
        return `
          <path d="M${x - w / 2} ${y} L${x - w / 3} ${y - h / 2} L${x + w / 3} ${y - h / 2}
            L${x + w / 2} ${y} L${x + w / 3} ${y + h / 2} L${x - w / 3} ${y + h / 2} Z"
            fill="${el.selected ? "rgba(240,163,94,0.22)" : "rgba(30,40,52,0.75)"}"
            stroke="${el.selected ? "#f0a35e" : owner}" stroke-width="${el.selected ? 2.4 : 1.4}"
            stroke-dasharray="${el.selected ? "0" : "4 3"}"/>
          <text x="${x}" y="${y + 3.5}" text-anchor="middle" fill="${el.selected ? "#f0a35e" : "#d7deea"}"
            font-size="9.5" font-family="DM Sans, sans-serif" font-weight="${el.selected ? 700 : 400}">${esc(el.label || "")}</text>`;
      }
      if (el.t === "relation") {
        // 关系：目标是两方之间的一条「边」，不是任何一个点
        const x1 = px(el.x1), y1 = py(el.y1), x2 = px(el.x2), y2 = py(el.y2);
        const st = { ally: ["#5dce8f", "0"], enemy: ["#ef6b6b", "0"], marriage: ["#c78bff", "0"], none: ["#66758a", "5 4"] };
        const [col, dash] = st[el.state] || st.none;
        const mx = (x1 + x2) / 2, my = (y1 + y2) / 2;
        const text = String(el.label || "");
        const w = 14 + text.length * 9;
        return `
          <line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" stroke="${col}" stroke-width="${el.selected ? 3.2 : 2}" stroke-dasharray="${dash}"/>
          <rect x="${mx - w / 2}" y="${my - 9}" width="${w}" height="18" rx="9"
            fill="#0b0e13" stroke="${el.selected ? "#f0a35e" : col}" stroke-width="${el.selected ? 2.2 : 1.3}"/>
          <text x="${mx}" y="${my + 4}" text-anchor="middle" fill="${el.selected ? "#f0a35e" : col}"
            font-size="9.5" font-family="DM Sans, sans-serif" font-weight="600">${esc(text)}</text>`;
      }
      if (el.t === "faction") {
        // 派系：抽象集合，没有位置。用影响力占比表示我能推动多少
        const x = px(el.x), y = py(el.y);
        const w = 78, h = 26;
        const share = Math.max(0, Math.min(1, el.influence == null ? 0.4 : el.influence));
        return `
          <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="4"
            fill="${el.selected ? "rgba(240,163,94,0.16)" : "#0b0e13"}"
            stroke="${el.selected ? "#f0a35e" : "#6ec8ff"}" stroke-width="${el.selected ? 2 : 1.2}"/>
          <text x="${x + 6}" y="${y + 12}" fill="${el.selected ? "#f0a35e" : "#e8eef6"}" font-size="9.5" font-family="DM Sans, sans-serif" font-weight="600">${esc(el.label || "派系")}</text>
          <rect x="${x + 6}" y="${y + 17}" width="${w - 12}" height="5" rx="2.5" fill="#1a2430" stroke="#2c3645" stroke-width="0.8"/>
          <rect x="${x + 7}" y="${y + 18}" width="${(w - 14) * share}" height="3" rx="1.5" fill="#5dce8f"/>`;
      }
      if (el.t === "pool") {
        // 抽象代价：威望 / 影响力 / 统治力，不是蓝条
        const x = px(el.x), y = py(el.y);
        const w = 72, h = 9;
        const have = Math.max(0, Math.min(1, el.have == null ? 0.7 : el.have));
        const cost = Math.max(0, Math.min(1, el.cost == null ? 0 : el.cost));
        const enough = have >= cost;
        return `
          <text x="${x}" y="${y - 5}" fill="#93a0b4" font-size="9" font-family="DM Sans, sans-serif">${esc(el.label || "影响力")}</text>
          <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="4" fill="#0b0e13" stroke="#3a4658" stroke-width="1"/>
          <rect x="${x + 1}" y="${y + 1}" width="${(w - 2) * have}" height="${h - 2}" rx="3" fill="${enough ? "#c78bff" : "#ef6b6b"}"/>
          ${cost > 0 ? `<rect x="${x + 1}" y="${y + 1}" width="${(w - 2) * cost}" height="${h - 2}" rx="3"
              fill="none" stroke="${enough ? "#e0c35a" : "#ef6b6b"}" stroke-width="1.6" stroke-dasharray="3 2"/>
            <text x="${x + w + 5}" y="${y + h}" fill="${enough ? "#e0c35a" : "#ef6b6b"}" font-size="8.5" font-family="DM Sans, sans-serif">要花这段</text>` : ""}`;
      }
      if (el.t === "lawslot") {
        // 法令槽位：目标是规则本身
        const x = px(el.x), y = py(el.y);
        const rows = el.rows || [];
        const w = 92, rh = 17;
        let out = `<text x="${x + 2}" y="${y - 4}" fill="#66758a" font-size="8.5" font-family="IBM Plex Mono, monospace">法令</text>`;
        rows.forEach((r, i) => {
          const ry = y + i * rh;
          const on = el.active === i;
          const locked = r.locked;
          const col = locked ? "#66758a" : on ? "#f0a35e" : "#6ec8ff";
          out += `
            <rect x="${x}" y="${ry}" width="${w}" height="${rh - 3}" rx="3"
              fill="${on ? "rgba(240,163,94,0.16)" : "#0b0e13"}" stroke="${col}" stroke-width="${on ? 2 : 1.1}"
              stroke-dasharray="${locked ? "3 2" : "0"}"/>
            <text x="${x + 6}" y="${ry + 10}" fill="${col}" font-size="9" font-family="DM Sans, sans-serif" font-weight="${on ? 700 : 400}">${esc(r.name)}</text>
            ${locked ? `<text x="${x + w - 6}" y="${ry + 10}" text-anchor="end" fill="#66758a" font-size="8.5" font-family="DM Sans, sans-serif">未解锁</text>` : ""}`;
        });
        return out;
      }
      if (el.t === "delaymark") {
        // 延迟生效：这一下不是现在结算，是 N 回合后
        const x = px(el.x), y = py(el.y);
        return `
          <circle cx="${x}" cy="${y}" r="11" fill="#0b0e13" stroke="#e0c35a" stroke-width="1.8"/>
          <line x1="${x}" y1="${y}" x2="${x}" y2="${y - 6}" stroke="#e0c35a" stroke-width="1.6"/>
          <line x1="${x}" y1="${y}" x2="${x + 5}" y2="${y + 2}" stroke="#e0c35a" stroke-width="1.6"/>
          <text x="${x}" y="${y + 22}" text-anchor="middle" fill="#e0c35a" font-size="9" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.label || `${el.turns} 回合后`)}</text>`;
      }
      if (el.t === "toast") {
        // 系统通知：飘一行字，和可点的菜单区分开（玩家不该想去点它）
        const x = px(el.x), y = py(el.y);
        const palette = { info: "#93a0b4", error: "#ef6b6b", gain: "#5dce8f", loss: "#e0c35a" };
        const col = palette[el.kind] || palette.info;
        const text = String(el.text || "");
        const w = 16 + text.length * 9;
        return `
          <rect x="${x}" y="${y - 11}" width="${w}" height="19" rx="9.5"
            fill="rgba(11,14,19,0.9)" stroke="${col}" stroke-width="1.2" stroke-dasharray="0"/>
          <circle cx="${x + 10}" cy="${y - 1.5}" r="3" fill="${col}"/>
          <text x="${x + 18}" y="${y + 2.5}" fill="${col}" font-size="10" font-family="DM Sans, sans-serif">${esc(text)}</text>`;
      }
      if (el.t === "partyframe") {
        // 队伍头像框：竖排头像 + 血条，当前友好目标高亮。不是菜单，别用 menu 凑
        const x = px(el.x), y = py(el.y);
        const rows = el.rows || [];
        const w = 74, rh = 22;
        let out = `<text x="${x + 2}" y="${y - 4}" fill="#66758a" font-size="8.5" font-family="IBM Plex Mono, monospace">队伍</text>`;
        rows.forEach((r, i) => {
          const ry = y + i * rh;
          const on = el.target === i;
          const hp = Math.max(0, Math.min(1, r.hp == null ? 1 : r.hp));
          const hpColor = hp < 0.35 ? "#ef6b6b" : hp < 0.7 ? "#e0c35a" : "#5dce8f";
          out += `
            <rect x="${x}" y="${ry}" width="${w}" height="${rh - 4}" rx="3"
              fill="${on ? "rgba(240,163,94,0.16)" : "#0b0e13"}"
              stroke="${on ? "#f0a35e" : "#3a4658"}" stroke-width="${on ? 2 : 1.2}"/>
            <circle cx="${x + 10}" cy="${ry + 9}" r="6" fill="#5a6b80" stroke="#0b0e13" stroke-width="1"/>
            <text x="${x + 21}" y="${ry + 8}" fill="${on ? "#f0a35e" : "#e8eef6"}" font-size="9" font-family="DM Sans, sans-serif" font-weight="${on ? 700 : 400}">${esc(r.name)}</text>
            <rect x="${x + 21}" y="${ry + 11}" width="${w - 27}" height="4" rx="2" fill="#1a2430" stroke="#2c3645" stroke-width="0.8"/>
            <rect x="${x + 22}" y="${ry + 12}" width="${(w - 29) * hp}" height="2" rx="1" fill="${hpColor}"/>`;
        });
        return out;
      }
      if (el.t === "roster") {
        const x = px(el.x), y = py(el.y);
        const rows = el.rows || [];
        const w = 96, rh = 16, h = 24 + rows.length * rh;
        const mark = { ready: ["✓", "#5dce8f"], waiting: ["…", "#e0c35a"], offline: ["×", "#ef6b6b"] };
        const items = rows.map((r, i) => {
          const [sym, col] = mark[r.state] || mark.waiting;
          const ry = y + 30 + i * rh;
          return `
            <text x="${x + 8}" y="${ry}" fill="#e8eef6" font-size="10" font-family="DM Sans, sans-serif">${esc(r.name)}</text>
            <text x="${x + w - 10}" y="${ry}" text-anchor="end" fill="${col}" font-size="11" font-family="DM Sans, sans-serif" font-weight="700">${sym}</text>`;
        }).join("");
        return `
          <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="4" fill="#0b0e13" stroke="#6ec8ff" stroke-width="1.4"/>
          <text x="${x + 8}" y="${y + 13}" fill="#66758a" font-size="8.5" font-family="IBM Plex Mono, monospace">${esc(el.title || "房间")}</text>
          ${items}`;
      }
      if (el.t === "netstat") {
        const x = px(el.x), y = py(el.y);
        const c = el.state === "lag" ? "#e0c35a" : el.state === "lost" ? "#ef6b6b" : "#5dce8f";
        const bars = [5, 8, 11, 14].map((bh, i) => {
          const lit = el.state === "lost" ? i < 1 : el.state === "lag" ? i < 2 : i < 4;
          return `<rect x="${x + i * 5}" y="${y - bh}" width="3.4" height="${bh}" fill="${lit ? c : "#2c3645"}"/>`;
        }).join("");
        return `${bars}
          <text x="${x + 24}" y="${y}" fill="${c}" font-size="9.5" font-family="IBM Plex Mono, monospace" font-weight="700">${esc(el.ping)}ms</text>`;
      }
      if (el.t === "voice") {
        const x = px(el.x), y = py(el.y);
        const on = el.state === "talking" || el.state === "on";
        const c = el.state === "talking" ? "#5dce8f" : el.state === "on" ? "#6ec8ff" : "#66758a";
        const waves = el.state === "talking"
          ? `<path d="M${x + 9} ${y - 5} q4 5 0 10" fill="none" stroke="${c}" stroke-width="1.4"/>
             <path d="M${x + 13} ${y - 8} q6 8 0 16" fill="none" stroke="${c}" stroke-width="1.1" opacity="0.7"/>`
          : "";
        return `
          <rect x="${x - 3.5}" y="${y - 10}" width="7" height="12" rx="3.5" fill="${on ? c : "#1a2430"}" stroke="${c}" stroke-width="1.4"/>
          <path d="M${x - 6} ${y + 1} q6 7 12 0" fill="none" stroke="${c}" stroke-width="1.4"/>
          <line x1="${x}" y1="${y + 4}" x2="${x}" y2="${y + 8}" stroke="${c}" stroke-width="1.4"/>
          ${el.state === "off" ? `<line x1="${x - 9}" y1="${y + 8}" x2="${x + 9}" y2="${y - 11}" stroke="#ef6b6b" stroke-width="1.8"/>` : ""}
          ${waves}`;
      }
      if (el.t === "vote") {
        const x = px(el.x), y = py(el.y);
        const need = el.need || 5;
        const yes = el.yes || 0;
        const w = 90, h = 8;
        return `
          <text x="${x}" y="${y - 6}" fill="#93a0b4" font-size="9.5" font-family="DM Sans, sans-serif">${esc(el.label || "表决")} ${yes}/${need}</text>
          <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="3" fill="#0b0e13" stroke="#3a4658" stroke-width="1"/>
          <rect x="${x + 1}" y="${y + 1}" width="${(w - 2) * Math.min(1, yes / need)}" height="${h - 2}" rx="2" fill="${yes >= need ? "#5dce8f" : "#e0c35a"}"/>
          ${Array.from({ length: need - 1 }, (_, i) => `<line x1="${x + ((i + 1) * w) / need}" y1="${y}" x2="${x + ((i + 1) * w) / need}" y2="${y + h}" stroke="#0b0e13" stroke-width="1.4"/>`).join("")}`;
      }
      if (el.t === "camera") {
        const x = px(el.x), y = py(el.y);
        const a = ((el.angle || 0) * Math.PI) / 180;
        const lock = el.mode === "lock";
        const c = lock ? "#f0a35e" : "#6ec8ff";
        const len = 30, half = (26 * Math.PI) / 180;
        const x1 = x + Math.cos(a - half) * len, y1 = y + Math.sin(a - half) * len;
        const x2 = x + Math.cos(a + half) * len, y2 = y + Math.sin(a + half) * len;
        return `
          <path d="M${x} ${y} L${x1} ${y1} A${len} ${len} 0 0 1 ${x2} ${y2} Z"
            fill="${lock ? "rgba(240,163,94,0.14)" : "rgba(110,200,255,0.12)"}" stroke="${c}"
            stroke-width="1.2" stroke-dasharray="${lock ? "0" : "4 3"}"/>
          <rect x="${x - 7}" y="${y - 5}" width="14" height="10" rx="2" fill="#1a2430" stroke="${c}" stroke-width="1.5"/>
          <circle cx="${x + Math.cos(a) * 8}" cy="${y + Math.sin(a) * 8}" r="2.6" fill="${c}"/>
          <text x="${x}" y="${y + 18}" text-anchor="middle" fill="${c}" font-size="8.5" font-family="DM Sans, sans-serif">${lock ? "镜头·跟身" : "镜头·自由"}</text>`;
      }
      if (el.t === "queue") {
        const x = px(el.x), y = py(el.y);
        const state = el.state || "waiting";
        const c = state === "active" ? "#f0a35e" : state === "done" ? "#5dce8f" : "#66758a";
        return `
          <circle cx="${x}" cy="${y}" r="8" fill="#0b0e13" stroke="${c}" stroke-width="1.8"/>
          <text x="${x}" y="${y + 3.5}" text-anchor="middle" fill="${c}" font-size="10" font-family="DM Sans, sans-serif" font-weight="700">${state === "done" ? "✓" : esc(el.n)}</text>
          <text x="${x}" y="${y - 12}" text-anchor="middle" fill="${c}" font-size="8.5" font-family="DM Sans, sans-serif">${state === "active" ? "在放" : state === "done" ? "放完" : "排队"}</text>`;
      }
      if (el.t === "held") {
        // 固定摆在画面左上（让开左上角文字标签）：手里拿着什么，永远在同一个位置看
        const x = SX + 10;
        const y = SY + 50;
        const w = 46, h = 26;
        const empty = !el.label;
        return `
          <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="4"
            fill="${empty ? "rgba(11,14,19,0.7)" : "#1a2430"}"
            stroke="${empty ? "#3a4658" : "#6ec8ff"}" stroke-width="1.4"
            stroke-dasharray="${empty ? "3 2" : "0"}"/>
          <text x="${x + 5}" y="${y - 4}" fill="#66758a" font-size="8.5" font-family="IBM Plex Mono, monospace" letter-spacing="0.5">手持</text>
          <text x="${x + w / 2}" y="${y + h / 2 + 4}" text-anchor="middle"
            fill="${empty ? "#66758a" : "#e8eef6"}" font-size="10.5" font-family="DM Sans, sans-serif" font-weight="${empty ? 400 : 700}">${esc(el.label || "空手")}</text>`;
      }
      if (el.t === "impact") {
        const x = px(el.x), y = py(el.y);
        const r = el.r || 16;
        const spikes = Array.from({ length: 10 }, (_, i) => {
          const a = (i / 10) * Math.PI * 2;
          return `<line x1="${x + Math.cos(a) * r * 0.72}" y1="${y + Math.sin(a) * r * 0.72}"
            x2="${x + Math.cos(a) * r * 1.28}" y2="${y + Math.sin(a) * r * 1.28}"
            stroke="#f0a35e" stroke-width="${el.heavy ? 2.6 : 1.7}"/>`;
        }).join("");
        return `
          <circle cx="${x}" cy="${y}" r="${r * 0.72}" fill="rgba(240,163,94,0.4)" stroke="#f0a35e" stroke-width="${el.heavy ? 2.6 : 1.8}"/>
          ${spikes}`;
      }
      if (el.t === "deny") {
        const x = px(el.x), y = py(el.y);
        const r = el.r || 11;
        const d = r * 0.62;
        return `
          <circle cx="${x}" cy="${y}" r="${r}" fill="rgba(239,107,107,0.16)" stroke="#ef6b6b" stroke-width="2.4"/>
          <line x1="${x - d}" y1="${y + d}" x2="${x + d}" y2="${y - d}" stroke="#ef6b6b" stroke-width="2.4"/>
          ${el.label ? `<text x="${x}" y="${y + r + 11}" text-anchor="middle" fill="#ef6b6b" font-size="9.5" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.label)}</text>` : ""}`;
      }
      if (el.t === "npc") {
        const x = px(el.x), y = py(el.y);
        const mark = { vendor: "商", quest: "?", healer: "魂", auction: "拍", trainer: "训" }[el.role] || "";
        return `
          <ellipse cx="${x}" cy="${y + 11}" rx="9" ry="3.5" fill="#000" opacity="0.35"/>
          <circle cx="${x}" cy="${y - 6}" r="4.5" fill="#e0c35a" stroke="#0b0e13" stroke-width="1.2"/>
          <path d="M${x - 6} ${y + 9} L${x - 4} ${y - 1} L${x + 4} ${y - 1} L${x + 6} ${y + 9} Z" fill="#e0c35a" stroke="#0b0e13" stroke-width="1.2"/>
          ${mark ? `<text x="${x}" y="${y - 13}" text-anchor="middle" fill="#e0c35a" font-size="10" font-family="DM Sans, sans-serif" font-weight="700">${esc(mark)}</text>` : ""}`;
      }
      if (el.t === "corpse") {
        const x = px(el.x), y = py(el.y);
        return `
          <ellipse cx="${x}" cy="${y}" rx="12" ry="5" fill="#3d4654" stroke="#0b0e13" stroke-width="1.2"/>
          <line x1="${x - 5}" y1="${y - 4}" x2="${x + 5}" y2="${y - 4}" stroke="#93a0b4" stroke-width="1.4"/>
          <text x="${x}" y="${y - 9}" text-anchor="middle" fill="#93a0b4" font-size="9" font-family="DM Sans, sans-serif">尸体</text>`;
      }
      if (el.t === "cursor") {
        const x = px(el.x), y = py(el.y);
        const mode = el.mode || "idle";
        if (!CURSOR_MODES.includes(mode)) {
          throw new Error(`分镜光标状态未知：${mode}（只允许 ${CURSOR_MODES.join(" / ")}）— 修 catalog 生成脚本`);
        }
        if (mode === "aim") {
          // 施法专属光标：鼠标已变成技能准星，和 FPS 大准星区分开
          return `
            <g transform="translate(${x},${y})">
              <circle r="7.5" fill="none" stroke="#f0a35e" stroke-width="1.8"/>
              <line x1="-13" y1="0" x2="-9" y2="0" stroke="#f0a35e" stroke-width="1.8"/>
              <line x1="9" y1="0" x2="13" y2="0" stroke="#f0a35e" stroke-width="1.8"/>
              <line x1="0" y1="-13" x2="0" y2="-9" stroke="#f0a35e" stroke-width="1.8"/>
              <line x1="0" y1="9" x2="0" y2="13" stroke="#f0a35e" stroke-width="1.8"/>
              <circle r="1.8" fill="#f0a35e"/>
            </g>`;
        }
        const down = mode === "down" || mode === "drag";
        // 松手那一拍要看得出「刚点下去」：在落点画外扩的确认波纹
        const release = mode === "up"
          ? `<circle r="8" fill="none" stroke="#f0a35e" stroke-width="1.8" opacity="0.95"/>
             <circle r="13.5" fill="none" stroke="#f0a35e" stroke-width="1.1" opacity="0.45"/>`
          : "";
        return `
          <g transform="translate(${x},${y})">
            ${release}
            <path d="M0 0 L0 16 L4.5 13 L8 20 L11 18.5 L7.5 11.5 L13 11 Z" fill="${down ? "#f0a35e" : "#e8eef6"}" stroke="#0b0e13" stroke-width="1"/>
            ${mode === "drag" ? `<circle cx="2" cy="2" r="10" fill="none" stroke="#f0a35e" stroke-dasharray="2 2" opacity="0.7"/>` : ""}
          </g>`;
      }
      if (el.t === "box") {
        return `<rect x="${px(el.x)}" y="${py(el.y)}" width="${(Number(el.w) / 100) * SW}" height="${(Number(el.h) / 100) * SH}" fill="rgba(110,200,255,0.12)" stroke="#6ec8ff" stroke-width="1.6" stroke-dasharray="4 3"/>`;
      }
      if (el.t === "ring") {
        // finisher = 这个目标现在可以被终结，和「锁定了谁」要能分开
        if (el.kind === "finisher") {
          const r0 = el.r || 8;
          return `<ellipse cx="${px(el.x)}" cy="${py(el.y) + 14}" rx="${r0}" ry="${r0 * 0.42}"
            fill="none" stroke="#e0c35a" stroke-width="2.4" stroke-dasharray="5 3"/>
            <text x="${px(el.x)}" y="${py(el.y) + 14 + r0 * 0.42 + 11}" text-anchor="middle"
              fill="#e0c35a" font-size="8.5" font-family="DM Sans, sans-serif" font-weight="700">可终结</text>`;
        }
        const color = el.kind === "lock" ? "#ef6b6b" : el.kind === "buff" ? "#6ec8ff" : "#f0a35e";
        // Ground ring under the unit tip (top-down “脚下”), not around the torso.
        const r = el.r || 8;
        return `<ellipse cx="${px(el.x)}" cy="${py(el.y) + 14}" rx="${r}" ry="${r * 0.42}" fill="none" stroke="${color}" stroke-width="2.2"/>`;
      }
      if (el.t === "crosshair") {
        const x = px(el.x), y = py(el.y);
        const c = el.locked ? "#ef6b6b" : "#e8eef6";
        // spread=tight 开镜收拢 / wide 扫射扩散：准星张开度本身就是信息
        const k = el.spread === "tight" ? 0.55 : el.spread === "wide" ? 1.7 : 1;
        const r = 10 * k, inner = 6 * k, outer = 16 * k;
        return `
          <circle cx="${x}" cy="${y}" r="${r}" fill="none" stroke="${c}" stroke-width="1.6" stroke-dasharray="${el.spread === "wide" ? "4 3" : "0"}"/>
          <line x1="${x - outer}" y1="${y}" x2="${x - inner}" y2="${y}" stroke="${c}" stroke-width="1.6"/>
          <line x1="${x + inner}" y1="${y}" x2="${x + outer}" y2="${y}" stroke="${c}" stroke-width="1.6"/>
          <line x1="${x}" y1="${y - outer}" x2="${x}" y2="${y - inner}" stroke="${c}" stroke-width="1.6"/>
          <line x1="${x}" y1="${y + inner}" x2="${x}" y2="${y + outer}" stroke="${c}" stroke-width="1.6"/>
          ${el.spread ? `<text x="${x}" y="${y + outer + 12}" text-anchor="middle" fill="${c}" font-size="9" font-family="DM Sans, sans-serif">${el.spread === "tight" ? "收拢" : "扩散"}</text>` : ""}`;
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
        const stroke = el.ghost ? "#6ec8ff" : el.team ? teamColor(el.team) : "#0b0e13";
        const width = el.ghost || el.team ? 2 : 1.5;
        return `<rect x="${x - 12}" y="${y - 12}" width="24" height="24" rx="3" fill="${fill}" stroke="${stroke}" stroke-width="${width}" stroke-dasharray="${el.ghost ? "3 2" : "0"}"/>`;
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
      if (el.t === "touchpt") {
        const x = px(el.x), y = py(el.y);
        const kind = el.kind || "tap";
        const dot = (cx, cy) => `<circle cx="${cx}" cy="${cy}" r="6.5" fill="rgba(110,200,255,0.35)" stroke="#6ec8ff" stroke-width="2"/>`;
        if (kind === "pinch") {
          const x2 = px(el.x2), y2 = py(el.y2);
          return `
            <line x1="${x}" y1="${y}" x2="${x2}" y2="${y2}" stroke="#6ec8ff" stroke-width="1.4" stroke-dasharray="4 3"/>
            ${dot(x, y)}${dot(x2, y2)}
            <path d="M${(x + x2) / 2 - 9} ${(y + y2) / 2} L${(x + x2) / 2 - 3} ${(y + y2) / 2}" stroke="#f0a35e" stroke-width="2" marker-end="url(#${markerPrefix}-move)"/>
            <path d="M${(x + x2) / 2 + 9} ${(y + y2) / 2} L${(x + x2) / 2 + 3} ${(y + y2) / 2}" stroke="#f0a35e" stroke-width="2" marker-end="url(#${markerPrefix}-move)"/>`;
        }
        const hold = kind === "hold"
          ? `<circle cx="${x}" cy="${y}" r="12" fill="none" stroke="#f0a35e" stroke-width="1.6"/>
             <circle cx="${x}" cy="${y}" r="16.5" fill="none" stroke="#f0a35e" stroke-width="1" opacity="0.5"/>`
          : "";
        const tap = kind === "tap"
          ? `<circle cx="${x}" cy="${y}" r="12" fill="none" stroke="#6ec8ff" stroke-width="1.4" opacity="0.75"/>`
          : "";
        const drag = kind === "drag"
          ? `<circle cx="${x}" cy="${y}" r="12" fill="none" stroke="#6ec8ff" stroke-width="1.3" stroke-dasharray="3 3"/>`
          : "";
        return `${hold}${tap}${drag}${dot(x, y)}`;
      }
      if (el.t === "wasd") {
        const on = Array.isArray(el.active) ? el.active : [];
        const cx = SX + 40;
        const cy = SY + SH - 34;
        const s = 17, gap = 2.5;
        const cap = (label, gx, gy) => {
          const lit = on.includes(label);
          const x = cx + gx * (s + gap) - s / 2;
          const y = cy + gy * (s + gap) - s / 2;
          return `
            <rect x="${x}" y="${y}" width="${s}" height="${s}" rx="3"
              fill="${lit ? "rgba(240,163,94,0.22)" : "#0b0e13"}" stroke="${lit ? "#f0a35e" : "#3a4658"}" stroke-width="${lit ? 2 : 1.2}"/>
            <text x="${x + s / 2}" y="${y + s / 2 + 4}" text-anchor="middle" fill="${lit ? "#f0a35e" : "#66758a"}" font-size="10" font-family="IBM Plex Mono, monospace" font-weight="700">${label}</text>`;
        };
        return `${cap("W", 0, -1)}${cap("A", -1, 0)}${cap("S", 0, 0)}${cap("D", 1, 0)}`;
      }
      if (el.t === "wheel") {
        const x = px(el.x), y = py(el.y);
        const r = el.r || 30;
        const labels = el.labels || [];
        const n = Math.max(2, labels.length);
        const step = (Math.PI * 2) / n;
        let out = `<circle cx="${x}" cy="${y}" r="${r}" fill="rgba(11,14,19,0.78)" stroke="#6ec8ff" stroke-width="1.6"/>`;
        for (let i = 0; i < n; i++) {
          const a0 = -Math.PI / 2 + i * step;
          const a1 = a0 + step;
          const on = el.active === i;
          if (on) {
            const x0 = x + Math.cos(a0) * r, y0 = y + Math.sin(a0) * r;
            const x1 = x + Math.cos(a1) * r, y1 = y + Math.sin(a1) * r;
            out += `<path d="M${x} ${y} L${x0} ${y0} A${r} ${r} 0 0 1 ${x1} ${y1} Z" fill="rgba(240,163,94,0.28)" stroke="#f0a35e" stroke-width="1.6"/>`;
          } else {
            const xe = x + Math.cos(a0) * r, ye = y + Math.sin(a0) * r;
            out += `<line x1="${x}" y1="${y}" x2="${xe}" y2="${ye}" stroke="#2c3645" stroke-width="1"/>`;
          }
          const am = a0 + step / 2;
          const lx = x + Math.cos(am) * r * 0.62;
          const ly = y + Math.sin(am) * r * 0.62;
          out += `<text x="${lx}" y="${ly + 3.5}" text-anchor="middle" fill="${on ? "#f0a35e" : "#93a0b4"}" font-size="9.5" font-family="DM Sans, sans-serif" font-weight="${on ? 700 : 400}">${esc(labels[i] || "")}</text>`;
        }
        return out;
      }
      if (el.t === "prop") {
        const x = px(el.x), y = py(el.y);
        const s = 8;
        if (el.kind === "wall" || el.kind === "cover") {
          const w = 34, h = 14;
          return `
            <rect x="${x - w / 2}" y="${y - h / 2}" width="${w}" height="${h}" rx="2"
              fill="#5a6b80" stroke="#0b0e13" stroke-width="1.4"/>
            <line x1="${x - w / 2}" y1="${y}" x2="${x + w / 2}" y2="${y}" stroke="#0b0e13" stroke-width="1" opacity="0.5"/>
            ${el.label ? `<text x="${x}" y="${y - h / 2 - 5}" text-anchor="middle" fill="#e0c35a" font-size="9.5" font-family="DM Sans, sans-serif" font-weight="600">${esc(el.label)}</text>` : ""}`;
        }
        const glow = el.highlight
          ? `<circle cx="${x}" cy="${y}" r="${s + 5}" fill="none" stroke="#e0c35a" stroke-width="1.6" opacity="0.85"/>`
          : "";
        return `${glow}
          <polygon points="${x},${y - s} ${x + s},${y} ${x},${y + s} ${x - s},${y}" fill="#c9a27a" stroke="#0b0e13" stroke-width="1.3"/>
          ${el.label ? `<text x="${x}" y="${y - s - 5}" text-anchor="middle" fill="#e0c35a" font-size="9.5" font-family="DM Sans, sans-serif" font-weight="600">${esc(el.label)}</text>` : ""}`;
      }
      if (el.t === "anchor") {
        const x = px(el.x), y = py(el.y);
        return `
          <circle cx="${x}" cy="${y}" r="7" fill="none" stroke="#e0c35a" stroke-width="2.2"/>
          <line x1="${x - 10}" y1="${y}" x2="${x + 10}" y2="${y}" stroke="#e0c35a" stroke-width="1.4"/>
          <line x1="${x}" y1="${y - 10}" x2="${x}" y2="${y + 10}" stroke="#e0c35a" stroke-width="1.4"/>
          <circle cx="${x}" cy="${y}" r="2" fill="#e0c35a"/>`;
      }
      if (el.t === "badge") {
        const w = Math.min(150, 20 + String(el.text).length * 9);
        return `
          <rect x="${SX + 8}" y="${SY + 8}" width="${w}" height="22" rx="4" fill="#0b0e13" stroke="#f0a35e" stroke-width="1.3"/>
          <text x="${SX + 16}" y="${SY + 23}" fill="#f0a35e" font-size="11" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.text)}</text>`;
      }
      if (el.t === "card") {
        const x = px(el.x), y = py(el.y);
        const drag = !!el.dragging;
        const w = drag ? 36 : 30, h = drag ? 44 : 38;
        return `
          <rect x="${x - w / 2}" y="${y - h / 2}" width="${w}" height="${h}" rx="5"
            fill="${drag ? "#2a3a4e" : "#1a2430"}" stroke="${drag ? "#f0a35e" : "#6ec8ff"}" stroke-width="${drag ? 2 : 1.4}"/>
          <text x="${x}" y="${y - 2}" text-anchor="middle" fill="#e8eef6" font-size="11" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.label || "卡")}</text>
          ${el.cost != null ? `<text x="${x}" y="${y + 14}" text-anchor="middle" fill="#f0a35e" font-size="10" font-family="IBM Plex Mono, monospace">${esc(el.cost)}</text>` : ""}`;
      }
      if (el.t === "menu") {
        const x = px(el.x), y = py(el.y);
        const lines = el.lines || [];
        const h = 16 + lines.length * 16;
        const w = 72;
        const items = lines.map((ln, i) => {
          const on = el.active === i;
          const hit = on
            ? `<rect x="${x + 3}" y="${y + 6 + i * 16}" width="${w - 6}" height="15" rx="2" fill="rgba(240,163,94,0.22)" stroke="#f0a35e" stroke-width="1.2"/>`
            : "";
          return `${hit}<text x="${x + 8}" y="${y + 18 + i * 16}" fill="${on ? "#f0a35e" : "#e8eef6"}" font-size="11" font-family="DM Sans, sans-serif" font-weight="${on ? 700 : 400}">${esc(ln)}</text>`;
        }).join("");
        return `
          <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="4" fill="#0b0e13" stroke="#6ec8ff" stroke-width="1.4"/>
          ${items}`;
      }
      if (el.t === "hotbar") {
        const slots = el.slots || 4;
        const w = 24, h = 24, gap = 4;
        const total = slots * w + (slots - 1) * gap;
        const x0 = SX + SW / 2 - total / 2;
        const y0 = SY + SH - h - 5;
        const off = Array.isArray(el.off) ? el.off : [];
        // 键位标签由生成器按平台填好：键盘是 Q/W/E/R，手柄是面键，触控是序号
        if (!Array.isArray(el.labels) || el.labels.length < slots) {
          throw new Error("技能栏缺 labels（按平台的键位标签）— 重跑 catalog 生成脚本");
        }
        const labels = el.labels;
        // page = 整栏换了一套技能：所有格子换色 + 左侧标出这是哪一套
        let out = el.page
          ? `<rect x="${x0 - 46}" y="${y0 + 3}" width="40" height="18" rx="3" fill="#0b0e13" stroke="#5dce8f" stroke-width="1.3"/>
             <text x="${x0 - 26}" y="${y0 + 16}" text-anchor="middle" fill="#5dce8f" font-size="9.5" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.page)}</text>`
          : "";
        for (let i = 0; i < slots; i++) {
          const x = x0 + i * (w + gap);
          const isActive = el.active === i;
          const isNew = el.extra === i || !!el.page;
          const isOff = off.includes(i);
          const stroke = isActive ? "#f0a35e" : isNew ? "#5dce8f" : "#3a4658";
          out += `<rect x="${x}" y="${y0}" width="${w}" height="${h}" rx="4" fill="${isOff ? "#10141a" : "#1a2430"}" stroke="${stroke}" stroke-width="${isActive || isNew ? 2 : 1.2}"/>`;
          out += `<text x="${x + w / 2}" y="${y0 + h / 2 + 4}" text-anchor="middle" fill="${isOff ? "#3a4658" : isActive ? "#f0a35e" : isNew ? "#5dce8f" : "#66758a"}" font-size="10" font-family="IBM Plex Mono, monospace" font-weight="700">${labels[i] || i + 1}</text>`;
          if (el.cd === i) {
            out += `<path d="M${x + w / 2} ${y0 + h / 2} L${x + w / 2} ${y0 + 2.5} A${w / 2 - 2.5} ${h / 2 - 2.5} 0 1 1 ${x + 2.5} ${y0 + h / 2} Z" fill="rgba(11,14,19,0.72)"/>`;
          }
          if (el.gcd) {
            // 全局冷却：所有格子压一层薄的，和单技能的转圈遮罩区分开
            out += `<rect x="${x}" y="${y0 + h * 0.62}" width="${w}" height="${h * 0.38}" fill="rgba(110,200,255,0.3)"/>`;
          }
          if (isOff) {
            out += `<line x1="${x + 4}" y1="${y0 + 4}" x2="${x + w - 4}" y2="${y0 + h - 4}" stroke="#ef6b6b" stroke-width="1.6"/>`;
          }
          if (el.dot === i) {
            out += `<circle cx="${x + w - 4.5}" cy="${y0 + 4.5}" r="3.2" fill="#5dce8f" stroke="#0b0e13" stroke-width="1"/>`;
          }
          if (el.deny === i) {
            out += `<rect x="${x}" y="${y0}" width="${w}" height="${h}" rx="4" fill="rgba(239,107,107,0.22)" stroke="#ef6b6b" stroke-width="1.6"/>`;
          }
          if (el.defer === i) {
            // 让路 ≠ 不可用：黄框加「让」，别用红叉否则意思正好相反
            out += `<rect x="${x}" y="${y0}" width="${w}" height="${h}" rx="4" fill="rgba(224,195,90,0.18)" stroke="#e0c35a" stroke-width="1.6"/>`;
            out += `<text x="${x + w / 2}" y="${y0 - 4}" text-anchor="middle" fill="#e0c35a" font-size="8.5" font-family="DM Sans, sans-serif" font-weight="700">让路</text>`;
          }
        }
        if (el.gcd) {
          out += `<text x="${x0 - 8}" y="${y0 + h / 2 + 4}" text-anchor="end" fill="#6ec8ff" font-size="8.5" font-family="DM Sans, sans-serif" font-weight="700">全局CD</text>`;
        }
        return out;
      }
      if (el.t === "bar") {
        const x = px(el.x == null ? 50 : el.x), y = py(el.y == null ? 30 : el.y);
        const w = 56, h = 7;
        const ratio = Math.max(0, Math.min(1, el.ratio == null ? 0.6 : el.ratio));
        const color = el.kind === "hp" ? "#5dce8f" : el.kind === "charge" ? "#f0a35e"
          : el.kind === "shield" ? "#a9dcff" : el.kind === "stamina" ? "#e0c35a"
          : el.kind === "boost" ? "#c78bff" : "#6ec8ff";
        let out = `
          <rect x="${x - w / 2}" y="${y}" width="${w}" height="${h}" rx="3" fill="#0b0e13" stroke="#3a4658" stroke-width="1"/>
          <rect x="${x - w / 2 + 1}" y="${y + 1}" width="${(w - 2) * ratio}" height="${h - 2}" rx="2" fill="${color}"/>`;
        if (el.broken) {
          out += `<line x1="${x - w / 2 - 3}" y1="${y + h + 3}" x2="${x + w / 2 + 3}" y2="${y - 3}" stroke="#ef6b6b" stroke-width="2"/>`;
        }
        if (el.label) {
          out += `<text x="${x}" y="${y - 3}" text-anchor="middle" fill="#93a0b4" font-size="9" font-family="DM Sans, sans-serif">${esc(el.label)}</text>`;
        }
        return out;
      }
      if (el.t === "key") {
        const x = px(el.x), y = py(el.y);
        const label = String(el.label || "F");
        const w = Math.max(20, 12 + label.length * 8), h = 20;
        const state = el.state || "idle";
        const stroke = state === "active" ? "#f0a35e" : state === "off" ? "#3a4658" : "#6ec8ff";
        const fill = state === "active" ? "rgba(240,163,94,0.18)" : "#0b0e13";
        const ink = state === "off" ? "#3a4658" : "#e8eef6";
        let out = `
          <rect x="${x - w / 2}" y="${y - h / 2}" width="${w}" height="${h}" rx="4" fill="${fill}" stroke="${stroke}" stroke-width="1.6"/>
          <rect x="${x - w / 2}" y="${y + h / 2 - 3}" width="${w}" height="3" rx="1.5" fill="${stroke}" opacity="0.5"/>
          <text x="${x}" y="${y + 4}" text-anchor="middle" fill="${ink}" font-size="11" font-family="IBM Plex Mono, monospace" font-weight="700">${esc(label)}</text>`;
        if (state === "off") {
          out += `<line x1="${x - w / 2 - 2}" y1="${y + h / 2 + 2}" x2="${x + w / 2 + 2}" y2="${y - h / 2 - 2}" stroke="#ef6b6b" stroke-width="1.6"/>`;
        }
        if (el.hint) {
          out += `<text x="${x}" y="${y - h / 2 - 5}" text-anchor="middle" fill="#f0a35e" font-size="10" font-family="DM Sans, sans-serif" font-weight="700">${esc(el.hint)}</text>`;
        }
        return out;
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
    if (!beat.view) throw new Error(`分镜缺 view（第 ${index + 1} 拍）— 修 catalog 生成脚本`);
    const stageLabel = viewLabel(beat.view);
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
        <text x="298" y="23" text-anchor="end" fill="#93a0b4" font-size="10" font-family="DM Sans, sans-serif">${esc(stageLabel)}</text>

        <!-- comic outer / inner frame -->
        <rect x="14" y="32" width="292" height="158" fill="none" stroke="#0b0e13" stroke-width="4"/>
        <rect x="16" y="34" width="288" height="154" fill="none" stroke="#c9a27a" stroke-width="1.2" opacity="0.55"/>

        <g clip-path="url(#clip-${id})">
          ${drawStage(beat.view, `gnd-${id}`)}
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
   * One Mermaid sequenceDiagram for ONE beat:
   * 设备输入 → 逻辑处理 → 画面输出（与右侧分镜一一对应）。
   */
  function sequenceMermaidBeat(b, i) {
    const logic = b.logic;
    if (!logic) {
      throw new Error(`beat T${i + 1} missing logic — regenerate catalog-data.js`);
    }
    const title = mmdText(b.title || `步骤 ${i + 1}`);
    return [
      "sequenceDiagram",
      "  participant I as 设备输入",
      "  participant L as 逻辑处理",
      "  participant O as 画面输出",
      `  Note over I,O: T${i + 1} ${title}`,
      `  I->>L: ${mmdText(b.input)}`,
      `  L->>L: ${mmdText(logic)}`,
      `  L->>O: ${mmdText(b.screen)}`,
    ].join("\n");
  }

  let mermaidReady = false;
  let mermaidSeq = 0;

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

  async function paintDetailMermaid() {
    const nodes = [...detailEl.querySelectorAll("pre.mermaid[data-pending]")];
    if (!nodes.length) return;
    ensureMermaid();
    for (const pre of nodes) {
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
  }

  function caseTargets(c) {
    if (!Array.isArray(c.targets) || !c.targets.length) {
      throw new Error(`case ${c.id || "?"} missing targets[] — regenerate catalog-data.js`);
    }
    return c.targets;
  }

  function actionTargets(a) {
    if (!Array.isArray(a.targets) || !a.targets.length) {
      throw new Error(`action ${a.key || "?"} missing targets[] — regenerate catalog-data.js`);
    }
    return a.targets;
  }

  function resolveCase(action, platform) {
    const variant = (action.variants || []).find((v) => v.platform === platform)
      || (action.variants || [])[0];
    if (!variant) throw new Error(`action ${action.key} has no variants`);
    const c = casesById[variant.caseId];
    if (!c) throw new Error(`case ${variant.caseId} missing for action ${action.key}`);
    return { case: c, platform: variant.platform, platformLabel: variant.platformLabel };
  }

  function pickPlatform(action, preferred) {
    const plats = (action.variants || []).map((v) => v.platform);
    if (preferred && plats.includes(preferred)) return preferred;
    return plats[0] || null;
  }

  function inActiveTargetAction(a) {
    if (activeCategory === "all") return true;
    return actionTargets(a).includes(activeCategory);
  }

  function filteredActions() {
    const q = query.trim().toLowerCase();
    return data.actions.filter((a) => {
      if (!q) return inActiveTargetAction(a);
      const targetTitles = actionTargets(a).map((id) => {
        const cat = data.categories.find((x) => x.id === id);
        return cat ? cat.title : id;
      });
      const variantBits = (a.variants || []).flatMap((v) => {
        const c = casesById[v.caseId];
        if (!c) return [v.caseId, v.platformLabel || v.platform];
        return [
          c.id, c.title, c.summary, c.platformLabel || "",
          ...c.beats.flatMap((b) => [b.input, b.logic, b.screen]),
        ];
      });
      const blob = [
        a.actionNo, a.key, a.title, a.summary,
        ...(a.genres || []), ...actionTargets(a), ...targetTitles,
        ...(a.platformLabels || []),
        ...variantBits,
      ].join(" ").toLowerCase();
      return blob.includes(q);
    });
  }

  function renderNav() {
    const items = [{ id: "all", title: "全部游戏", blurb: "不按复刻目标过滤" }, ...data.categories];
    navEl.innerHTML = items.map((cat) => {
      const count = cat.id === "all"
        ? data.actions.length
        : data.actions.filter((a) => actionTargets(a).includes(cat.id)).length;
      const blurb = cat.blurb ? `<span class="nav-blurb">${esc(cat.blurb)}</span>` : "";
      return `<button type="button" class="nav-btn ${activeCategory === cat.id ? "active" : ""}" data-cat="${esc(cat.id)}"><span class="nav-title">${esc(cat.title)} <span class="count">(${count})</span></span>${blurb}</button>`;
    }).join("");
    navEl.querySelectorAll("button").forEach((btn) => {
      btn.addEventListener("click", () => {
        activeCategory = btn.dataset.cat;
        selectedActionKey = null;
        selectedPlatform = null;
        activeBeat = 0;
        render();
      });
    });
  }

  function renderList(actions) {
    if (!actions.length) {
      listEl.innerHTML = `<div class="empty">没有匹配的动作。</div>`;
      return;
    }
    listEl.innerHTML = actions.map((a) => {
      const plats = (a.platformLabels || []).map((p) => `<span class="plat-pill">${esc(p)}</span>`).join("");
      return `
      <button type="button" class="case-row ${selectedActionKey === a.key ? "active" : ""}" data-key="${esc(a.key)}" title="${esc(a.actionNo)} · ${esc(a.key)}">
        <span class="title"><span class="action-no">${esc(a.actionNo)}</span> ${esc(a.title)}</span>
        <span class="beats">${a.caseCount > 1 ? `${a.caseCount} 端` : `${a.beatCount} 镜`}</span>
        <p class="sub">${esc(a.summary)}</p>
        <span class="plat-row">${plats}</span>
      </button>`;
    }).join("");
    listEl.querySelectorAll(".case-row").forEach((btn) => {
      btn.addEventListener("click", () => {
        selectedActionKey = btn.dataset.key;
        const action = actionsByKey[selectedActionKey];
        selectedPlatform = pickPlatform(action, selectedPlatform);
        activeBeat = 0;
        renderList(filteredActions());
        renderDetail();
        const active = listEl.querySelector(".case-row.active");
        if (active) active.scrollIntoView({ block: "nearest" });
      });
    });
  }

  function setActiveBeat(i, scroll = true) {
    activeBeat = i;
    detailEl.querySelectorAll(".beat-chip").forEach((el) => {
      el.classList.toggle("active", Number(el.dataset.beat) === activeBeat);
    });
    detailEl.querySelectorAll(".beat-pair[data-beat]").forEach((el) => {
      el.classList.toggle("active", Number(el.dataset.beat) === activeBeat);
    });
    if (scroll) {
      const pair = detailEl.querySelector(`.beat-pair[data-beat="${activeBeat}"]`);
      if (pair) pair.scrollIntoView({ block: "nearest", behavior: "smooth" });
    }
  }

  function renderDetail() {
    const action = selectedActionKey ? actionsByKey[selectedActionKey] : null;
    if (!action) {
      detailEl.innerHTML = `<div class="empty-detail">从中间列表点一个动作，这里按「一镜一对」展开时序与分镜；跨平台同一交互可切主机 / 键鼠 / 触控。</div>`;
      return;
    }
    selectedPlatform = pickPlatform(action, selectedPlatform);
    const resolved = resolveCase(action, selectedPlatform);
    const c = resolved.case;
    selectedPlatform = resolved.platform;
    if (activeBeat >= c.beats.length) activeBeat = 0;
    const missingLogic = c.beats.findIndex((b) => !b.logic);
    if (missingLogic >= 0) {
      throw new Error(`${c.id} beat T${missingLogic + 1} missing logic — regenerate catalog-data.js`);
    }
    const targetTags = caseTargets(c).map((id) => {
      const cat = data.categories.find((x) => x.id === id);
      return `<span class="tag tag-target">${esc(cat ? cat.title : id)}</span>`;
    }).join("");
    const family = c.familyTitle || c.family || c.category || "";
    const todos = (c.todos || []).map((t) => `<li>${esc(t)}</li>`).join("");
    const chips = c.beats.map((b, i) =>
      `<button type="button" class="beat-chip ${i === activeBeat ? "active" : ""}" data-beat="${i}">T${i + 1} ${esc(b.title || "")}</button>`
    ).join("");
    const pairs = c.beats.map((b, i) => {
      const mmd = sequenceMermaidBeat(b, i);
      return `
      <section class="beat-pair ${i === activeBeat ? "active" : ""}" data-beat="${i}">
        <header class="beat-pair-head">
          <span class="beat-pair-title">T${i + 1} ${esc(b.title || "")}</span>
          <span class="beat-pair-hint">时序 · 分镜</span>
        </header>
        <div class="beat-pair-body">
          <div class="beat-seq" aria-label="第 ${i + 1} 拍时序">
            <pre class="mermaid" data-pending="1">${esc(mmd)}</pre>
          </div>
          <article class="panel" aria-label="第 ${i + 1} 拍分镜">
            ${storyboardSvg(b, i)}
            <div class="panel-cap">
              <div class="cap-row"><span class="k">设备</span><span class="v">${esc(b.input)}</span></div>
              <div class="cap-row"><span class="k">逻辑</span><span class="v">${esc(b.logic)}</span></div>
              <div class="cap-row"><span class="k">画面</span><span class="v">${esc(b.screen)}</span></div>
            </div>
          </article>
        </div>
      </section>`;
    }).join("");
    const multi = (action.variants || []).length > 1;
    const platformTabs = (action.variants || []).map((v) => `
      <button type="button" class="plat-tab ${v.platform === selectedPlatform ? "active" : ""}" data-platform="${esc(v.platform)}" aria-pressed="${v.platform === selectedPlatform ? "true" : "false"}">
        ${esc(v.platformLabel)}
        <span class="plat-tab-id">${esc(v.caseId)}</span>
      </button>`).join("");
    const platformBar = multi
      ? `<div class="plat-tabs" role="tablist" aria-label="平台实现">${platformTabs}</div>`
      : `<div class="plat-single"><span class="plat-pill">${esc(resolved.platformLabel)}</span><span class="plat-single-id">${esc(c.id)}</span></div>`;
    detailEl.innerHTML = `
      <div class="detail-inner">
        <div class="detail-top">
          <div class="detail-head">
            <h2><span class="action-no">${esc(action.actionNo)}</span> ${esc(action.title)}</h2>
            <div class="tags">${targetTags}${family ? `<span class="tag tag-family">${esc(family)}</span>` : ""}<span class="tag tag-platform">${esc(resolved.platformLabel)}</span></div>
            <span class="case-id">${esc(c.id)}</span>
          </div>
          ${platformBar}
          <div class="summary-row">
            <p class="summary">${esc(c.summary)}</p>
            <details class="impl-details">
              <summary>Ludots 现状 / TODO</summary>
              <div class="impl-line">
                <div class="impl-card">
                  <div class="section-label">Ludots 现状</div>
                  <p>${esc(c.ludots || "未标注")}</p>
                </div>
                <div class="impl-card impl-todo">
                  <div class="section-label">缺口 TODO</div>
                  ${todos ? `<ul>${todos}</ul>` : `<p class="ok">本条暂无额外 TODO</p>`}
                </div>
              </div>
            </details>
          </div>
        </div>
        <div class="beat-stage">
          <div class="beat-stage-head">
            <span>一镜一对 · ${esc(resolved.platformLabel)} · 共 ${c.beats.length} 拍（左时序 / 右分镜，一起滚）</span>
            <span class="beat-rail" aria-label="拍号">${chips}</span>
          </div>
          <div class="beat-pairs" id="beat-pairs" aria-label="时序与分镜对照">${pairs}</div>
        </div>
      </div>`;
    detailEl.querySelectorAll(".plat-tab").forEach((el) => {
      el.addEventListener("click", () => {
        selectedPlatform = el.dataset.platform;
        activeBeat = 0;
        renderDetail();
      });
    });
    detailEl.querySelectorAll(".beat-chip").forEach((el) => {
      el.addEventListener("click", (e) => {
        e.stopPropagation();
        setActiveBeat(Number(el.dataset.beat));
      });
    });
    detailEl.querySelectorAll(".beat-pair[data-beat]").forEach((el) => {
      el.addEventListener("click", () => setActiveBeat(Number(el.dataset.beat), false));
    });
    paintDetailMermaid().catch((err) => console.error(err));
  }

  function render() {
    renderNav();
    const actions = filteredActions();
    if (!selectedActionKey || !actions.some((a) => a.key === selectedActionKey)) {
      selectedActionKey = actions[0] ? actions[0].key : null;
      selectedPlatform = selectedActionKey
        ? pickPlatform(actionsByKey[selectedActionKey], selectedPlatform)
        : null;
      activeBeat = 0;
    }
    const multi = data.actions.filter((a) => a.caseCount > 1).length;
    statsEl.innerHTML = `
      <span class="chip">唯一动作 <strong>${data.actions.length}</strong></span>
      <span class="chip">跨平台 <strong>${multi}</strong></span>
      <span class="chip">实现 <strong>${data.cases.length}</strong></span>
      <span class="chip">分镜 <strong>${data.cases.reduce((n, c) => n + c.beats.length, 0)}</strong></span>
      <span class="chip">本栏 <strong>${actions.length}</strong></span>`;
    renderList(actions);
    renderDetail();
  }

  let searchTimer = 0;
  searchEl.addEventListener("input", (e) => {
    query = e.target.value;
    window.clearTimeout(searchTimer);
    searchTimer = window.setTimeout(() => { render(); }, 120);
  });

  // Keyboard: j/k or ↑↓ 换动作；[ / ] 换平台；h/l 或 ←→ 换拍（一镜一对）
  document.addEventListener("keydown", (e) => {
    if (e.target === searchEl) return;
    const action = selectedActionKey ? actionsByKey[selectedActionKey] : null;
    const resolved = action ? resolveCase(action, selectedPlatform) : null;
    const c = resolved ? resolved.case : null;
    if ((e.key === "[" || e.key === "]") && action && (action.variants || []).length > 1) {
      e.preventDefault();
      const plats = action.variants.map((v) => v.platform);
      const idx = Math.max(0, plats.indexOf(selectedPlatform));
      const next = e.key === "]"
        ? Math.min(plats.length - 1, idx + 1)
        : Math.max(0, idx - 1);
      selectedPlatform = plats[next];
      activeBeat = 0;
      renderDetail();
      return;
    }
    if ((e.key === "ArrowLeft" || e.key === "h" || e.key === "ArrowRight" || e.key === "l") && c) {
      e.preventDefault();
      const delta = (e.key === "ArrowRight" || e.key === "l") ? 1 : -1;
      const next = Math.max(0, Math.min(c.beats.length - 1, activeBeat + delta));
      setActiveBeat(next);
      return;
    }
    if (e.key !== "ArrowDown" && e.key !== "ArrowUp" && e.key !== "j" && e.key !== "k") return;
    const actions = filteredActions();
    if (!actions.length) return;
    e.preventDefault();
    const idx = Math.max(0, actions.findIndex((x) => x.key === selectedActionKey));
    const next = (e.key === "ArrowDown" || e.key === "j")
      ? Math.min(actions.length - 1, idx + 1)
      : Math.max(0, idx - 1);
    selectedActionKey = actions[next].key;
    selectedPlatform = pickPlatform(actions[next], selectedPlatform);
    activeBeat = 0;
    renderList(actions);
    renderDetail();
    const active = listEl.querySelector(".case-row.active");
    if (active) active.scrollIntoView({ block: "nearest" });
  });

  render();
})();


