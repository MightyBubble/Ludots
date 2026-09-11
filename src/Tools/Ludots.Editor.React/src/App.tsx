import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import { EditorLayout } from "@/components/Editor/EditorLayout";
import { GasGraphEditorPage } from "@/pages/GasGraphEditorPage";
import { AiTopologyEditorPage } from "@/pages/AiTopologyEditorPage";
import { UiPanelAuthoringPage } from "@/pages/UiPanelAuthoringPage";
import { StoryAuthoringPage } from "@/pages/StoryAuthoringPage";
import { AuthoringShell } from "@/pages/authoring-studio/AuthoringShell";
import { AuthoringStudioHome } from "@/pages/authoring-studio/AuthoringStudioHome";

export default function App() {
  return (
    <Router>
      <Routes>
        <Route element={<AuthoringShell />}>
          <Route path="/" element={<AuthoringStudioHome />} />
          <Route path="/blueprint" element={<GasGraphEditorPage key="func" dialect="func" />} />
          <Route path="/gas-graphs" element={<GasGraphEditorPage key="func" dialect="func" />} />
          <Route path="/bt-editor" element={<AiTopologyEditorPage key="bt" kind="behavior-trees" />} />
          <Route path="/fsm-editor" element={<AiTopologyEditorPage key="fsm" kind="hfsm" />} />
          <Route path="/dialogue" element={<StoryAuthoringPage tool="dialogue" />} />
          <Route path="/timeline" element={<StoryAuthoringPage tool="timeline" />} />
          <Route path="/story-authoring" element={<StoryAuthoringPage />} />
        </Route>
        <Route path="/map" element={<EditorLayout />} />
        <Route path="/ui-panel-authoring" element={<UiPanelAuthoringPage />} />
      </Routes>
    </Router>
  );
}
