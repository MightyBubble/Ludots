import { createRoot } from 'react-dom/client';
import { App } from './App.jsx';
import './styles.css';

const root = document.getElementById('root');
if (!root) {
  throw new Error('Live Skill Workbench root element #root is missing.');
}

createRoot(root).render(<App />);
