import React from 'react';
import { useEditorStore } from './EditorStore';
import { HexRenderer } from './HexRenderer';
import { Toolbar } from './Toolbar';
import { WorkspaceLayout } from '@/components/ui/WorkspaceLayout';

export const EditorLayout: React.FC = () => {
    const loading = useEditorStore((s) => s.loadingState);
    const error = useEditorStore((s) => s.error);
    const sessionLabel = useEditorStore((s) => s.canvasSessionLabel);

    return (
        <WorkspaceLayout
            title="地图"
            blurb="六边形地形、笔刷、实体摆放、导航烘焙；工具面板贴着画布。"
            status={loading.isLoading ? `${loading.message} ${loading.progress}%` : sessionLabel ?? ''}
            error={error ?? undefined}
        >
            <div className="relative h-full w-full overflow-hidden bg-studio-bg">
                <HexRenderer />
                <Toolbar />
            </div>
        </WorkspaceLayout>
    );
};
