import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import { EditorLayout } from "@/components/Editor/EditorLayout";
import { GasGraphEditorPage } from "@/pages/GasGraphEditorPage";
import { UiPanelAuthoringPage } from "@/pages/UiPanelAuthoringPage";

export default function App() {
  return (
    <Router>
      <Routes>
        <Route path="/" element={<EditorLayout />} />
        <Route path="/gas-graphs" element={<GasGraphEditorPage />} />
        <Route path="/ui-panel-authoring" element={<UiPanelAuthoringPage />} />
      </Routes>
    </Router>
  );
}
