import { enhanceDiagramImages } from './diagrams-flow.js';

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => enhanceDiagramImages(), { once: true });
} else {
  enhanceDiagramImages();
}
