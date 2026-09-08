import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import { EditorLayout } from "@/components/Editor/EditorLayout";
import { GasGraphEditorPage } from "@/pages/GasGraphEditorPage";
import { AiTopologyEditorPage } from "@/pages/AiTopologyEditorPage";
import { UiPanelAuthoringPage } from "@/pages/UiPanelAuthoringPage";
import { StoryAuthoringPage } from "@/pages/StoryAuthoringPage";

export default function App() {
  return (
    <Router>
      <Routes>
        <Route path="/" element={<EditorLayout />} />
        <Route path="/gas-graphs" element={<GasGraphEditorPage key="func" dialect="func" />} />
        <Route path="/bt-editor" element={<AiTopologyEditorPage key="bt" kind="behavior-trees" />} />
        <Route path="/fsm-editor" element={<AiTopologyEditorPage key="fsm" kind="hfsm" />} />
        <Route path="/ui-panel-authoring" element={<UiPanelAuthoringPage />} />
        <Route path="/story-authoring" element={<StoryAuthoringPage />} />
      </Routes>
    </Router>
  );
}
