/* ============================================================
   Ludots 门户站点共享脚本
   - 注入统一顶部导航（纯静态站点无模板，用 JS 保持各页一致）
   - 注入 docs 文档页（prd / tdd / reference）侧边栏导航
     （issue #695：31 页复制粘贴的 navData 收敛于此，退役占位页已剔除）
   - 基址自适应：门户页在站点根、docs 页在下一层子目录，
     统一用站点根基址拼链接；window.SITE_BASE 可显式覆盖
   - 通用工具：HTML 转义、复制按钮、懒取文本、marked 配置
   ============================================================ */
(function () {
  "use strict";

  /* ---------- 站点基址 ----------
     默认由本脚本自身的 URL 推导（…/site-assets/site.js → 站点根），
     GitHub Pages 项目站（/Ludots/）与本地 http.server 根（/）均可工作；
     页面也可在引入本脚本前设置 window.SITE_BASE 显式指定。 */
  var SITE_BASE = (function () {
    if (typeof window.SITE_BASE === "string") return window.SITE_BASE;
    var cur = document.currentScript;
    if (cur && cur.src) {
      var marker = "site-assets/site.js";
      var i = cur.src.indexOf(marker);
      if (i >= 0) return cur.src.slice(0, i);
    }
    return "";
  })();

  var NAV_ITEMS = [
    { href: "index.html", label: "门户", page: "home" },
    { href: "index.html#docs", label: "文档", page: "home", hash: "#docs" },
    { href: "graph-op-wiki.html", label: "Graph 节点画廊", page: "graphop" },
    { href: "gallery.html", label: "Showcase 画廊", page: "gallery" },
    { href: "tests.html", label: "测试与验收", page: "tests" },
    { href: "diagrams.html", label: "架构图库", page: "diagrams" }
  ];

  /* docs 文档页侧栏目录（原 31 页各自硬编码的 navData 收敛于此；
     已退役的占位页条目一并剔除，侧栏不再出现死链） */
  var DOCS_NAV = [
    { group: "PRD", items: [
      ["prd/00-executive-summary.html", "Executive Summary"],
      ["prd/01-product-overview.html", "Product Overview"],
      ["prd/03-core-engine.html", "Core Engine"],
      ["prd/04-gas-combat.html", "Gas Combat"],
      ["prd/05-items-inventory.html", "Items Inventory"],
      ["prd/06-narrative.html", "Narrative"],
      ["prd/07-relationships-teams.html", "Relationships Teams"],
      ["prd/11-map-spatial.html", "Map Spatial"],
      ["prd/12-config-scripting.html", "Config Scripting"],
      ["prd/13-modding.html", "Modding"],
      ["prd/14-persistence.html", "Persistence"],
      ["prd/15-platform-adapters.html", "Platform Adapters"]
    ]},
    { group: "TDD", items: [
      ["tdd/00-architecture-principles.html", "Architecture Principles"],
      ["tdd/01-data-model.html", "Data Model"],
      ["tdd/02-systemgroup.html", "Systemgroup"],
      ["tdd/04-pipeline-design.html", "Pipeline Design"],
      ["tdd/05-extension-points.html", "Extension Points"],
      ["tdd/06-gas-tdd.html", "Gas Tdd"]
    ]},
    { group: "Reference", items: [
      ["reference/api-quickref.html", "Api Quickref"]
    ]},
    { group: "Other", items: [
      ["index.html", "Index"],
      ["diagrams.html", "Diagrams"]
    ]}
  ];

  function injectTopbar() {
    var host = document.getElementById("topbar");
    if (!host) return;
    var page = document.body.getAttribute("data-page") || "";
    var html = '<div class="topbar-inner">';
    html += '<a class="brand" href="' + SITE_BASE + 'index.html"><span class="dot"></span>Ludots</a>';
    html += '<button class="menu-btn" id="menu-btn" aria-label="菜单">☰</button>';
    html += '<nav id="topnav">';
    for (var i = 0; i < NAV_ITEMS.length; i++) {
      var it = NAV_ITEMS[i];
      var active = it.page === page && !it.hash ? " class=\"active\"" : "";
      html += '<a href="' + SITE_BASE + it.href + '"' + active + ">" + it.label + "</a>";
    }
    html += "</nav>";
    html += '<a class="gh" href="https://github.com/mightybubble/ludots" target="_blank" rel="noopener">GitHub ↗</a>';
    html += "</div>";
    host.className = "topbar";
    host.innerHTML = html;

    var btn = document.getElementById("menu-btn");
    var nav = document.getElementById("topnav");
    if (btn && nav) {
      btn.addEventListener("click", function () {
        nav.classList.toggle("open");
      });
    }
  }

  /* docs 文档页侧栏：页面以 <body data-docs-page="prd/xx.html"> 声明当前页 */
  function buildDocsNav() {
    var host = document.getElementById("nav");
    if (!host) return;
    var current = document.body.getAttribute("data-docs-page") || "";
    if (!current) return;
    var html = "";
    for (var i = 0; i < DOCS_NAV.length; i++) {
      var g = DOCS_NAV[i];
      if (!g.items.length) continue;
      html += '<div class="group"><div class="group-title">' + esc(g.group) + "</div>";
      for (var j = 0; j < g.items.length; j++) {
        var href = g.items[j][0];
        var title = g.items[j][1];
        var cls = href === current ? ' class="active"' : "";
        html += '<a href="' + SITE_BASE + href + '"' + cls + ">" + esc(title) + "</a>";
      }
      html += "</div>";
    }
    host.innerHTML = html;
  }

  /* ---------- 工具 ---------- */

  function esc(s) {
    return String(s == null ? "" : s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function fetchText(url) {
    return fetch(url).then(function (r) {
      if (!r.ok) throw new Error("HTTP " + r.status + " for " + url);
      return r.text();
    });
  }

  /* 绑定页面上所有 .copy-btn：复制 data-copy 或相邻 .cmd 文本 */
  function bindCopyButtons(root) {
    (root || document).querySelectorAll(".copy-btn").forEach(function (btn) {
      if (btn.__bound) return;
      btn.__bound = true;
      btn.addEventListener("click", function () {
        var text = btn.getAttribute("data-copy");
        if (!text) {
          var holder = btn.closest(".cmdline");
          var cmd = holder && holder.querySelector(".cmd");
          text = cmd ? cmd.textContent : "";
        }
        var done = function () {
          var old = btn.textContent;
          btn.textContent = "已复制";
          setTimeout(function () { btn.textContent = old; }, 1200);
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(text).then(done, done);
        } else {
          var ta = document.createElement("textarea");
          ta.value = text;
          document.body.appendChild(ta);
          ta.select();
          try { document.execCommand("copy"); } catch (e) { /* ignore */ }
          document.body.removeChild(ta);
          done();
        }
      });
    });
  }

  /* marked 全局配置：页面各自引入 marked.min.js 后调用 */
  function configMarked() {
    if (window.marked && marked.setOptions) {
      marked.setOptions({ gfm: true, breaks: false, headerIds: true, mangle: false });
    }
  }

  /* 命令行块快捷构造 */
  function cmdlineHtml(cmd) {
    return '<div class="cmdline"><span class="cmd">' + esc(cmd) + '</span>' +
      '<button class="copy-btn" data-copy="' + esc(cmd) + '">复制</button></div>';
  }

  document.addEventListener("DOMContentLoaded", function () {
    injectTopbar();
    buildDocsNav();
    configMarked();
    bindCopyButtons(document);
  });

  window.LudotsSite = {
    SITE_BASE: SITE_BASE,
    esc: esc,
    fetchText: fetchText,
    bindCopyButtons: bindCopyButtons,
    cmdlineHtml: cmdlineHtml,
    configMarked: configMarked,
    buildDocsNav: buildDocsNav
  };
})();
