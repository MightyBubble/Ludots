import React from 'react';
import { HexRenderer } from './HexRenderer';
import { Toolbar } from './Toolbar';

export const EditorLayout: React.FC = () => {
    return (
        <div className="w-screen h-screen bg-black overflow-hidden relative">
            <HexRenderer />
            <Toolbar />
        </div>
    );
};
