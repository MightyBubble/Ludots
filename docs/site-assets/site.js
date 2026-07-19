/* ============================================================
   Ludots 门户站点共享脚本
   - 注入统一顶部导航（纯静态站点无模板，用 JS 保持四页一致）
   - 通用工具：HTML 转义、复制按钮、懒取文本、marked 配置
   ============================================================ */
(function () {
  "use strict";

  var NAV_ITEMS = [
    { href: "index.html", label: "门户", page: "home" },
    { href: "index.html#docs", label: "文档", page: "home", hash: "#docs" },
    { href: "gallery.html", label: "Showcase 画廊", page: "gallery" },
    { href: "tests.html", label: "测试与验收", page: "tests" },
    { href: "diagrams.html", label: "架构图库", page: "diagrams" }
  ];

  function injectTopbar() {
    var host = document.getElementById("topbar");
    if (!host) return;
    var page = document.body.getAttribute("data-page") || "";
    var html = '<div class="topbar-inner">';
    html += '<a class="brand" href="index.html"><span class="dot"></span>Ludots</a>';
    html += '<button class="menu-btn" id="menu-btn" aria-label="菜单">☰</button>';
    html += '<nav id="topnav">';
    for (var i = 0; i < NAV_ITEMS.length; i++) {
      var it = NAV_ITEMS[i];
      var active = it.page === page && !it.hash ? " class=\"active\"" : "";
      html += '<a href="' + it.href + '"' + active + ">" + it.label + "</a>";
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
    configMarked();
    bindCopyButtons(document);
  });

  window.LudotsSite = {
    esc: esc,
    fetchText: fetchText,
    bindCopyButtons: bindCopyButtons,
    cmdlineHtml: cmdlineHtml,
    configMarked: configMarked
  };
})();
