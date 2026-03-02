import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import { EditorLayout } from "@/components/Editor/EditorLayout";

export default function App() {
  return (
    <Router>
      <Routes>
        <Route path="/" element={<EditorLayout />} />
      </Routes>
    </Router>
  );
}
