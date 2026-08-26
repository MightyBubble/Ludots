import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import { EditorLayout } from "@/components/Editor/EditorLayout";
import { GasGraphEditorPage } from "@/pages/GasGraphEditorPage";
import { UiPanelAuthoringPage } from "@/pages/UiPanelAuthoringPage";
import { StoryAuthoringPage } from "@/pages/StoryAuthoringPage";
import { TimelineAuthoringPage } from "@/pages/timeline/TimelineAuthoringPage";

export default function App() {
  return (
    <Router>
      <Routes>
        <Route path="/" element={<EditorLayout />} />
        <Route path="/gas-graphs" element={<GasGraphEditorPage />} />
        <Route path="/ui-panel-authoring" element={<UiPanelAuthoringPage />} />
        <Route path="/story-authoring" element={<StoryAuthoringPage />} />
        <Route path="/timeline" element={<TimelineAuthoringPage />} />
      </Routes>
    </Router>
  );
}
