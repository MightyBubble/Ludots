import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";

const LUDOTS_RULES = `Ludots coding rules (do not skip):
- Search existing capabilities before writing code. Reuse Registry, Pipeline, System, and Mod hang points.
- If the work needs a new pipeline or Core interface change, stop and explain the plan first.
- No silent failure, no compatibility shims, no second copy of an existing source of truth.
- Comments explain non-obvious intent only. Do not leave issue or PR numbers in code comments.
- Gameplay stays in Mods. This assistant is tooling, not a runtime Mod.`;

export default function (pi: ExtensionAPI) {
  pi.on("before_agent_start", async (event) => ({
    systemPrompt: `${event.systemPrompt}\n\n${LUDOTS_RULES}`,
  }));

  pi.registerCommand("ludots-rules", {
    description: "Show the Ludots coding rules this assistant must follow",
    handler: async (_args, ctx) => {
      ctx.ui.notify(LUDOTS_RULES, "info");
    },
  });
}
