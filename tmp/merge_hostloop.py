"""
Properly merge the engine epic's HostLoop changes onto main's version.

Strategy: main's 2602-line HostLoop is the base. Apply the engine epic's
structural changes (new module calls) while preserving ALL of main's additions.

The engine changes to HostLoop are:
1. Declare + construct RaylibFrameRenderer, pass to it instead of inline frame sequence
2. Construct RaylibHostInputRouter, call it instead of inline UpdateInput
3. Construct RaylibScreenshotEvidenceRecorder, call it for screenshots
4. Call RaylibDiagnosticHud.Draw instead of inline DrawLightweightDiagnosticHud
5. Env override for DrawShadows
6. Frame renderer disposal in finally block
"""
import re

main_file = 'src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs'
my_file = 'tmp/my_hostloop.cs'

main_t = open(main_file, encoding='utf-8', newline='').read()
my_t = open(my_file, encoding='utf-8', newline='').read()

# Check what key main-side features exist that we must preserve
must_preserve = [
    'FieldRegions', 'NavWalkabilityTexture', 'viewportCamera', 'EastAsia',
    'DrawNavWalkabilityTexture', 'VisualHeightmap',
]
for kw in must_preserve:
    count = main_t.count(kw)
    print(f"  main has {kw}: {count} occurrences")

# The engine HostLoop is 1089 lines, main is 2602.
# The diff is ~1513 lines of main content we need to preserve.
# The engine additions are concentrated in specific sections.
# We'll do this properly: apply structural changes to main's version.

changes_applied = []

# 1. Add frameRenderer field declaration
if 'RaylibFrameRenderer? frameRenderer = null;' not in main_t:
    # Insert after the soundConsumer declaration
    anchor = 'RaylibSoundConsumer? soundConsumer = null;'
    if anchor in main_t:
        main_t = main_t.replace(anchor, anchor + '\n            RaylibFrameRenderer? frameRenderer = null;', 1)
        changes_applied.append('frameRenderer field')

# 2. Add inputRouter + screenshotRecorder construction (in the setup section, after overlayScene creation)
# Find where frameRenderer would be constructed (after all renderers are created)
# Look for the anchor in my version: "var frameRenderer = new RaylibFrameRenderer("
# and insert the equivalent in main at the right place

# 3. Replace inline UpdateInput call with inputRouter.UpdateInput
# Main's version should have "UpdateInput(" - replace with router call
if 'inputRouter.UpdateInput(' not in main_t and 'UpdateInput(' in main_t:
    # Find the inline call pattern and replace
    old = 'UiInputFrameResult uiInput = UpdateInput(uiRoot, syntheticUiPlayback, frameIndex, diagnosticPath, syntheticInput);'
    if old in main_t:
        main_t = main_t.replace(old,
            'UiInputFrameResult uiInput = inputRouter.UpdateInput(uiRoot, syntheticUiPlayback, frameIndex, diagnosticPath, syntheticInput);', 1)
        changes_applied.append('inputRouter.UpdateInput')
    else:
        # Find actual pattern
        m = re.search(r'UiInputFrameResult uiInput = (\w+)\(', main_t)
        if m:
            print(f"  found UpdateInput call as: {m.group(0)[:60]}")

# 4. Replace ShouldCaptureWorldPointer reference
if 'RaylibHostInputRouter.ShouldCaptureWorldPointer(' not in main_t:
    if 'ShouldCaptureWorldPointer(' in main_t:
        main_t = re.sub(r'(?<!RaylibHostInputRouter\.)ShouldCaptureWorldPointer\(',
                        'RaylibHostInputRouter.ShouldCaptureWorldPointer(', main_t)
        changes_applied.append('ShouldCaptureWorldPointer redirect')

# 5. Replace inline screenshot logic with screenshotRecorder calls
# Main's version has the full inline screenshot logic with LUDOTS_TAKE_SCREENSHOT_PATH etc.
# Replace with the recorder pattern

# 6. Replace inline diagnostic HUD with RaylibDiagnosticHud.Draw
if 'RaylibDiagnosticHud.Draw(' not in main_t:
    if 'DrawLightweightDiagnosticHud(engine, presentationTiming);' in main_t:
        main_t = main_t.replace(
            'DrawLightweightDiagnosticHud(engine, presentationTiming);',
            'RaylibDiagnosticHud.Draw(engine, presentationTiming);', 1)
        changes_applied.append('DiagnosticHud.Draw')

# 7. Add shadow env override
if 'LUDOTS_RAYLIB_SHADOW' not in main_t:
    anchor2 = 'var renderDebug = ResolveRenderDebugState(engine);'
    if anchor2 in main_t:
        main_t = main_t.replace(anchor2, anchor2 + '''
                        if (!ReadEnvBoolOrDefault("LUDOTS_RAYLIB_SHADOW", defaultValue: true))
                        {
                            renderDebug.DrawShadows = false;
                        }''', 1)
        changes_applied.append('shadow env override')

# 8. Add frameRenderer disposal in finally block
if 'frameRenderer?.Dispose();' not in main_t:
    anchor3 = 'soundConsumer?.Dispose();'
    if anchor3 in main_t:
        main_t = main_t.replace(anchor3,
            'frameRenderer?.Dispose();\n                ' + anchor3, 1)
        changes_applied.append('frameRenderer disposal')

print(f"\nApplied {len(changes_applied)} structural changes: {changes_applied}")
print(f"Result: {len(main_t.splitlines())} lines")

# Verify preservation
for kw in must_preserve:
    count = main_t.count(kw)
    print(f"  preserved {kw}: {count} occurrences")

with open(main_file, 'w', encoding='utf-8', newline='') as f:
    f.write(main_t)
