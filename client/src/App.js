import React from 'react';
import AppProviders from './app/AppProviders';
import AppRouter from './app/AppRouter';
import './App.css';

function App() {
  return (
    <AppProviders>
      <AppRouter />
    </AppProviders>
  );
}

export default App;
