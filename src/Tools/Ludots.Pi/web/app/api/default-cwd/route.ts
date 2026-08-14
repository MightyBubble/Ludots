import { NextResponse } from "next/server";
import { allowFileRoot } from "@/lib/file-access";
import { resolveLudotsWorkspace } from "@/lib/ludots-workspace";

export async function POST() {
  try {
    const cwd = resolveLudotsWorkspace();
    allowFileRoot(cwd);
    return NextResponse.json({ cwd });
  } catch (error) {
    return NextResponse.json({ error: String(error) }, { status: 500 });
  }
}
