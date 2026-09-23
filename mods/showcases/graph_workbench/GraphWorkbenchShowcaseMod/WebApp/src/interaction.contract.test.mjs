import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const sourcePath = fileURLToPath(new URL('./main.jsx', import.meta.url));
const source = readFileSync(sourcePath, 'utf8');

test('ReactFlow owns node double-click navigation in embedded CEF', () => {
  assert.match(source, /onNodeDoubleClick=\{onNodeDoubleClick\}/);
  assert.match(source, /nodeClickDistance=\{6\}/);
  assert.match(source, /nodeDragThreshold=\{4\}/);
  assert.match(source, /connectOnClick=\{false\}/);
  assert.doesNotMatch(source, /onDoubleClick=\{\(event\) => \{\s*event\.stopPropagation\(\);/);
});
